using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;

namespace DevAudit;

/// <summary>
/// Сквозная проверка синхронизации на живых процессах (без GUI):
///   dotnet run --project DevAudit -- --sync-e2e URL_ПЕРВОЙ_МАШИНЫ URL_ВТОРОЙ_МАШИНЫ URL_СЕРВЕРА_СИНХРОНИЗАЦИИ
/// Две «машины» — два Backend с разными папками данных. Код возврата 0 = всё прошло.
/// Скриншоты окна: --sync-shots DIR (см. <see cref="SyncShots"/>).
/// </summary>
public static class SyncScenarios
{
    public static void Run(string[] args)
    {
        if (args[0] == "--sync-shots")
        {
            SyncShots.Run(args[1]);
            return;
        }
        Environment.ExitCode = RunE2EAsync(args[1], args.Length > 2 ? args[2] : "", args.Length > 3 ? args[3] : "")
            .GetAwaiter().GetResult();
    }

    private static async Task<int> RunE2EAsync(string machineA, string machineB, string syncServer)
    {
        // настройки клиента (адрес и логин) — во временную папку, не в профиль пользователя
        Environment.SetEnvironmentVariable(ClientSettings.DataDirEnvironmentVariable,
            Path.Combine(Path.GetTempPath(), "swine-sync-e2e-" + Guid.NewGuid().ToString("N")));

        var failures = 0;
        async Task Step(string name, Func<Task> action)
        {
            try
            {
                await action();
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            }
        }

        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        using var a = new FileManagerApiClient(machineA);
        using var b = new FileManagerApiClient(machineB);
        var user = "e2e-" + Guid.NewGuid().ToString("N")[..8];
        var password = Guid.NewGuid().ToString("N");
        var names = new[] { "sync-demo-1", "sync-demo-2" };
        var ids = new List<Guid>();

        await Step("A: сессия пуста, адрес не задан", async () =>
        {
            var s = await a.GetCloudSessionAsync();
            Check(!s.LoggedIn, "ожидали, что вход не выполнен");
        });
        await Step("A: вход в несуществующий аккаунт → 401", async () =>
        {
            try { await a.CloudLoginAsync(syncServer, user, password); }
            catch (ApiException ex) when (ex.StatusCode == 401) { return; }
            throw new Exception("ожидали 401");
        });
        await Step("A: недоступный сервер → 502, не 500", async () =>
        {
            try { await a.CloudLoginAsync("http://127.0.0.1:1", user, password); }
            catch (ApiException ex) when (ex.StatusCode == 502) { return; }
            throw new Exception("ожидали 502");
        });
        await Step("A: регистрация", () => a.CloudRegisterAsync(syncServer, user, password));
        await Step("A: повторная регистрация того же логина → ошибка", async () =>
        {
            try { await a.CloudRegisterAsync(syncServer, user, password); }
            catch (ApiException ex) when (ex.StatusCode == 400) { return; }
            throw new Exception("ожидали 400");
        });
        await Step("A: вход, сессия знает адрес и логин", async () =>
        {
            await a.CloudLoginAsync(syncServer, user, password);
            var s = await a.GetCloudSessionAsync();
            Check(s.LoggedIn && s.Username == user, "сессия не открылась");
            Check(string.Equals(s.ServerUrl, syncServer.TrimEnd('/'), StringComparison.OrdinalIgnoreCase), $"адрес в сессии: {s.ServerUrl}");
        });
        await Step("A: два профиля созданы и отправлены", async () =>
        {
            foreach (var name in names)
            {
                var p = SavedProfile.Create(name, new HostProfile(string.Empty, Protocol.Local, new AnonymousAuth()));
                ids.Add(await a.SaveProfileAsync(p));
            }
            foreach (var id in ids)
                await a.CloudUploadProfileAsync(id);
        });
        await Step("B («вторая машина»): профилей нет", async () =>
            Check((await b.GetProfileIdsAsync()).Count == 0, "на второй машине уже есть профили"));

        // вторая машина — через тот же SyncViewModel, что и в окне
        var settings = new ClientSettings();
        var refreshed = 0;
        var vm = new SyncViewModel(b, settings, () => { refreshed++; return Task.CompletedTask; })
        {
            ServerUrl = syncServer,
            Username = user,
            Password = password,
        };
        await Step("B: «Войти» в окне синхронизации", async () =>
        {
            await vm.SignInCommand.ExecuteAsync(null);
            Check(vm.IsSignedIn && vm.SignedInUser == user, $"не вошли: {vm.Title} / {vm.Text}");
            Check(vm.Password.Length == 0, "пароль остался в форме");
            Check(settings.SyncServerUrl == vm.SignedInServer && settings.SyncLogin == user, "адрес и логин не запомнены");
        });
        await Step("B: «Забрать профили» → профили на месте, список обновлён", async () =>
        {
            await vm.DownloadCommand.ExecuteAsync(null);
            Check(vm.Panel == SyncPanel.Done, $"состояние {vm.Panel}: {vm.Title} / {vm.Text}");
            Check(refreshed == 1, "список профилей не обновлён");
            var got = await b.GetProfileIdsAsync();
            Check(ids.All(got.Contains) && got.Count == ids.Count, $"получили {got.Count} профилей вместо {ids.Count}");
            foreach (var id in ids)
            {
                var p = await b.GetProfileAsync(id);
                Check(names.Contains(p.Name), "имя профиля не совпало");
            }
        });
        await Step("B: «Выйти», затем скачивание → 401", async () =>
        {
            await vm.SignOutCommand.ExecuteAsync(null);
            Check(!vm.IsSignedIn, "остались вошедшими");
            try { await b.CloudDownloadProfilesAsync(); }
            catch (ApiException ex) when (ex.StatusCode == 401) { return; }
            throw new Exception("ожидали 401");
        });
        await Step("Чистка: тестовые профили удалены с обеих машин", async () =>
        {
            foreach (var id in ids)
            {
                await a.DeleteProfileAsync(id);
                await b.DeleteProfileAsync(id);
            }
        });

        Console.WriteLine(failures == 0 ? "Синхронизация: всё прошло." : $"Синхронизация: провалов {failures}.");
        return failures == 0 ? 0 : 1;
    }
}
