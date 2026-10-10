using System.Text;
using FileManagerClient.Api;
using FileManagerClient.Models;

namespace FileManagerClient;

/// <summary>
/// Самопроверка API-клиента против живого Backend (без GUI):
/// FileManagerClient.exe --smoke http://127.0.0.1:5117
/// Прогоняет весь контракт через Local-протокол (песочница "tmp" на сервере),
/// отчёт пишет в smoke-result.txt и в stdout, код возврата 0 = всё прошло.
/// </summary>
public static class SmokeTest
{
    private const string ReportFile = "smoke-result.txt";

    public static async Task<int> RunAsync(string baseUrl)
    {
        var report = new StringBuilder();
        var failures = 0;

        void Step(string name, Action action)
        {
            try
            {
                action();
                report.AppendLine($"[PASS] {name}");
                Console.Out.WriteLine($"[PASS] {name}");
            }
            catch (Exception raw)
            {
                // .Wait()/.Result оборачивают в AggregateException — разворачиваем
                var ex = raw is AggregateException ae ? (ae.InnerExceptions.Count > 0 ? ae.InnerException ?? raw : raw) : raw;
                failures++;
                report.AppendLine($"[FAIL] {name}: {ex.Message}");
                Console.Out.WriteLine($"[FAIL] {name}: {ex.Message}");
            }
        }

        report.AppendLine($"Сервер: {baseUrl}");
        report.AppendLine($"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine();

        using var api = new FileManagerApiClient(baseUrl);

        // Зачистка артефактов прошлого прогона (best effort — их может не быть)
        void Cleanup()
        {
            try
            {
                var ids = api.GetProfileIdsAsync().Result;
                foreach (var id in ids)
                {
                    try
                    {
                        var profile = api.GetProfileAsync(id).Result;
                        if (profile.Name.StartsWith("ui-smoke-"))
                        {
                            api.DeleteProfileAsync(id).Wait();
                        }
                    }
                    catch
                    {
                        // профиль мог не читаться — пропускаем
                    }
                }
            }
            catch
            {
                // сервер недоступен — дальше всё равно упадёт с понятной ошибкой
            }
        }

        Cleanup();

        // Профили
        Step("GET /profiles (список id)", () => api.GetProfileIdsAsync().Wait());

        var tempProfile = SavedProfile.Create(
            $"ui-smoke-{Guid.NewGuid():N}"[..40],
            new HostProfile(string.Empty, Protocol.Local, new AnonymousAuth()));
        Step("POST /profiles (создание Local-профиля)", () =>
        {
            var id = api.SaveProfileAsync(tempProfile).Result;
            if (id != tempProfile.Id)
                throw new Exception($"ожидали id {tempProfile.Id}, получили {id}");
        });

        Step("GET /profiles/{id} (чтение профиля, полиморфный Auth)", () =>
        {
            var loaded = api.GetProfileAsync(tempProfile.Id).Result;
            if (loaded.Name != tempProfile.Name
                || loaded.HostProfile.Protocol != Protocol.Local
                || loaded.HostProfile.Auth is not AnonymousAuth)
                throw new Exception($"профиль прочитан неверно: {loaded}");
        });

        // Соединение
        var connectionId = Guid.Empty;
        Step("POST /connections (создание из профиля)", () =>
            connectionId = api.CreateConnectionAsync(tempProfile).Result);
        Step("POST /connections/{id}/connect", () => api.ConnectAsync(connectionId).Wait());

        // Файловые артефакты прошлого прогона в песочнице (best effort)
        foreach (var leftover in new[] { "ui-smoke.txt", "ui-smoke-renamed.txt", "ui-smoke-copy.txt", "ui-smoke-moved.txt", "ui-smoke-uploaded.txt" })
        {
            try
            {
                api.DeleteFileAsync(connectionId, leftover).Wait();
            }
            catch
            {
                // нет и хорошо
            }
        }
        try
        {
            api.DeleteDirAsync(connectionId, "ui-smoke-dir").Wait();
        }
        catch
        {
            // нет и хорошо
        }
        Step("GET /connections/{id}/state == true", () =>
        {
            if (!api.GetConnectionStateAsync(connectionId).Result)
                throw new Exception("соединение не подключено");
        });

        // Файловые операции (Local-протокол: рабочая директория — песочница "tmp")
        var workingDir = string.Empty;
        Step("GET filesystem/dir/current", () =>
        {
            workingDir = api.GetWorkingDirectoryAsync(connectionId).Result;
            if (workingDir != string.Empty)
                throw new Exception($"ожидали пустой корень, получили «{workingDir}»");
        });
        Step("POST filesystem/file (создание ui-smoke.txt)", () =>
            api.CreateFileAsync(connectionId, "ui-smoke.txt").Wait());

        Step("GET filesystem (листинг, разбор FileItem)", () =>
        {
            var items = api.GetFilesAsync(connectionId).Result;
            var file = items.FirstOrDefault(f => f.Name == "ui-smoke.txt")
                       ?? throw new Exception("ui-smoke.txt не найден в листинге");
            if (file.IsDirectory || file.FullPath != "ui-smoke.txt")
                throw new Exception($"FileItem разобран неверно: {file.FullPath}, dir={file.IsDirectory}");
        });

        Step("GET filesystem/info (path в query)", () =>
        {
            var info = api.GetFileInfoAsync(connectionId, "ui-smoke.txt").Result;
            if (info.Name != "ui-smoke.txt")
                throw new Exception($"info вернул {info.Name}");
        });

        Step("PATCH filesystem/file (переименование)", () =>
            api.RenameFileAsync(connectionId, "ui-smoke.txt", "ui-smoke-renamed.txt").Wait());
        Step("POST filesystem/file/copy", () =>
            api.CopyFileAsync(connectionId, "ui-smoke-renamed.txt", "ui-smoke-copy.txt").Wait());
        Step("PATCH filesystem/file/move", () =>
            api.MoveFileAsync(connectionId, "ui-smoke-copy.txt", "ui-smoke-moved.txt").Wait());
        Step("GET filesystem/file/exists", () =>
        {
            if (!api.FileExistsAsync(connectionId, "ui-smoke-moved.txt").Result)
                throw new Exception("файл после move не найден");
        });

        // Спецсимволы в path: клиент должен кодировать query так же, как ждёт сервер
        foreach (var name in new[] { "ui-smoke a b.txt", "ui-smoke c++.txt", "ui-smoke #1.txt",
                     "ui-smoke 50%.txt", "ui-smoke a&b=c.txt", "ui-smoke файл.txt" })
        {
            Step($"path в query: «{name}» (create → exists → info → delete)", () =>
            {
                api.CreateFileAsync(connectionId, name).Wait();
                if (!api.FileExistsAsync(connectionId, name).Result)
                    throw new Exception("file/exists вернул false после создания");
                var info = api.GetFileInfoAsync(connectionId, name).Result;
                if (info.Name != name)
                    throw new Exception($"info вернул «{info.Name}»");
                api.DeleteFileAsync(connectionId, name).Wait();
                if (api.FileExistsAsync(connectionId, name).Result)
                    throw new Exception("file/exists вернул true после удаления");
            });
        }

        // Загрузка/скачивание
        var localUpload = Path.Combine(Path.GetTempPath(), "ui-smoke-upload.txt");
        var localDownload = Path.Combine(Path.GetTempPath(), "ui-smoke-download.txt");
        var payload = $"ui-smoke {DateTime.Now:O} — проверка upload/download";
        await File.WriteAllTextAsync(localUpload, payload);

        Step("POST filesystem/file/upload (multipart)", () =>
            api.UploadFileAsync(connectionId, "ui-smoke-uploaded.txt", localUpload).Wait());
        Step("GET filesystem/file/download + сравнение содержимого", () =>
        {
            api.DownloadFileAsync(connectionId, "ui-smoke-uploaded.txt", localDownload).Wait();
            var downloaded = File.ReadAllText(localDownload);
            if (downloaded != payload)
                throw new Exception($"содержимое не совпало: «{downloaded}»");
        });

        Step("POST filesystem/dir + навигация", () =>
            api.CreateDirAsync(connectionId, "ui-smoke-dir").Wait());
        Step("POST filesystem/file внутри папки", () =>
            api.CreateFileAsync(connectionId, "ui-smoke-dir/inner.txt").Wait());
        Step("GET filesystem/dir/size (новое в PR #29)", () =>
        {
            var size = api.GetDirSizeAsync(connectionId, "ui-smoke-dir").Result;
            if (size < 0)
                throw new Exception($"размер папки отрицательный: {size}");
        });
        Step("POST filesystem/dir/copy (новое в PR #29)", () =>
            api.CopyDirAsync(connectionId, "ui-smoke-dir", "ui-smoke-dir-copy").Wait());
        Step("PATCH filesystem/dir/move (новое в PR #29)", () =>
            api.MoveDirAsync(connectionId, "ui-smoke-dir-copy", "ui-smoke-dir-moved").Wait());
        Step("GET filesystem/dir/exists (копия на месте)", () =>
        {
            if (!api.DirExistsAsync(connectionId, "ui-smoke-dir-moved").Result)
                throw new Exception("папка после move не найдена");
        });
        Step("PATCH filesystem/dir/current (переход в папку)", () =>
            api.ChangeDirectoryAsync(connectionId, "ui-smoke-dir").Wait());
        Step("GET filesystem/dir/current (путь изменился)", () =>
        {
            var dir = api.GetWorkingDirectoryAsync(connectionId).Result;
            if (dir.TrimEnd('/') != "ui-smoke-dir")
                throw new Exception($"ожидали ui-smoke-dir, получили «{dir}»");
        });
        Step("PATCH filesystem/dir/current (возврат в корень через \"/\")", () =>
            api.ChangeDirectoryAsync(connectionId, "/").Wait());
        Step("GET filesystem/dir/current (снова корень)", () =>
        {
            var dir = api.GetWorkingDirectoryAsync(connectionId).Result;
            if (dir != string.Empty)
                throw new Exception($"ожидали корень, получили «{dir}»");
        });

        // Очистка
        Step("DELETE filesystem/file (renamed)", () =>
            api.DeleteFileAsync(connectionId, "ui-smoke-renamed.txt").Wait());
        Step("DELETE filesystem/file (moved)", () =>
            api.DeleteFileAsync(connectionId, "ui-smoke-moved.txt").Wait());
        Step("DELETE filesystem/file (uploaded)", () =>
            api.DeleteFileAsync(connectionId, "ui-smoke-uploaded.txt").Wait());
        Step("DELETE filesystem/dir", () =>
            api.DeleteDirAsync(connectionId, "ui-smoke-dir").Wait());
        Step("DELETE filesystem/dir (moved)", () =>
        {
            if (!api.DirExistsAsync(connectionId, "ui-smoke-dir-moved").Result)
                return;
            api.DeleteDirAsync(connectionId, "ui-smoke-dir-moved").Wait();
        });

        Step("POST /connections/{id}/disconnect", () => api.DisconnectAsync(connectionId).Wait());
        Step("DELETE /connections/{id}", () => api.DeleteConnectionAsync(connectionId).Wait());
        Step("DELETE /profiles/{id} (удаление временного профиля)", () =>
            api.DeleteProfileAsync(tempProfile.Id).Wait());

        try
        {
            File.Delete(localUpload);
            File.Delete(localDownload);
        }
        catch
        {
            // временные файлы — не критично
        }

        report.AppendLine();
        report.AppendLine(failures == 0
            ? "ИТОГ: все шаги пройдены"
            : $"ИТОГ: ошибок — {failures}");
        await File.WriteAllTextAsync(ReportFile, report.ToString());
        Console.Out.WriteLine();
        Console.Out.WriteLine($"Отчёт: {Path.GetFullPath(ReportFile)}");

        return failures == 0 ? 0 : 1;
    }
}
