using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Controls;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views.Dialogs;

namespace FileManagerClient.Commands;

/// <summary>
/// ЗАГЛУШКА источника плагинных команд. Сейчас команды в него кладёт клиентская демо-установка
/// («Посчитать SHA-256»). Когда появится бэкенд плагинов (фаза 1: PluginManager, GET /commands,
/// POST /commands/{id}/execute), этот класс заменяется источником, который загружает контрибуции
/// с сервера и строит AppCommand с Handler = вызов /commands/{id}/execute; остальной код не меняется.
/// </summary>
public sealed class PluginStubCommandSource : ICommandSource
{
    private readonly List<AppCommand> _commands = new();

    public IEnumerable<AppCommand> GetCommands() => _commands.ToList();

    public void Add(AppCommand command)
    {
        _commands.RemoveAll(c => c.Id == command.Id);
        _commands.Add(command);
    }

    public bool Contains(string id) => _commands.Any(c => c.Id == id);
}

/// <summary>Встроенные команды, привязанные к существующим командам MainWindowViewModel и FileBrowserViewModel.</summary>
public static class BuiltInCommands
{
    public const string ConnectPrefix = "Подключиться: ";
    public const string Sha256Id = "plugin.checksum.sha256";

    private const string NotBound = "Сначала подключитесь к серверу";

    public static void Register(CommandRegistry registry, MainWindowViewModel vm, IDialogService dialogs)
    {
        var b = vm.Browser;
        var pluginSource = new PluginStubCommandSource();

        string? NeedConnection(CommandContext? _) => b.IsBound ? null : NotBound;
        string? NeedSelection(CommandContext? _) =>
            !b.IsBound ? NotBound : b.HasSelection || b.SelectedItem is not null ? null : "Выберите файл или папку";
        string? NeedFileSelection(CommandContext? _) =>
            !b.IsBound ? NotBound : b.SelectedItem is { IsDirectory: false } || b.HasSelection ? null : "Выберите файл";

        AppCommand Make(string id, string title, string category, Func<CommandContext, Task> handler,
            string icon = "Line.Command", string? shortcut = null, Func<CommandContext?, string?>? disabled = null,
            params string[] keywords) => new()
        {
            Id = id, Title = title, Category = category, Handler = handler, IconKey = icon,
            Shortcut = shortcut, DisabledReason = disabled, Keywords = keywords,
        };

        // --- Подключение ---
        registry.Register(Make("server.connect", "Подключиться к серверу…", "Подключение", ctx =>
        {
            ctx.KeepPaletteOpen = true;
            ctx.NewQuery = ConnectPrefix;
            return Task.CompletedTask;
        }, "Line.ChevronRight", null, _ => vm.Profiles.Count > 0 ? null : "Нет сохранённых серверов", "connect", "хост", "ssh"));
        registry.Register(Make("server.disconnect", "Отключиться", "Подключение",
            _ => Run(vm.DisconnectProfileCommand, vm.ActiveProfile), "Line.Close", null,
            _ => vm.ActiveProfile is null ? "Нет активного подключения" : null, "disconnect"));
        registry.Register(Make("server.add", "Добавить сервер", "Подключение",
            _ => Run(vm.AddProfileCommand), "Line.Plus", null, null, "новый", "профиль", "add"));
        registry.Register(Make("server.edit", "Изменить профиль", "Подключение",
            _ => Run(vm.EditProfileCommand), "Line.FileText", null,
            _ => vm.SelectedProfile is null ? "Выберите сервер в списке" : null, "редактировать", "edit"));
        registry.Register(Make("server.settings", "Настройки сервера", "Приложение",
            _ => Run(vm.OpenServerSettingsCommand), "Line.Code", null, null, "адрес", "settings", "настройки"));

        // --- Навигация ---
        registry.Register(Make("nav.refresh", "Обновить", "Навигация", _ =>
                b.IsBound ? Run(b.RefreshCommand) : Run(vm.RefreshProfilesCommand),
            "Line.Refresh", null, null, "перечитать", "refresh", "reload"));
        registry.Register(Make("nav.up", "Вверх", "Навигация", _ => Run(b.GoUpCommand), "Line.Up", "Backspace",
            NeedConnection, "родительская", "up"));
        registry.Register(Make("nav.back", "Назад", "Навигация", _ => Run(b.GoBackCommand), "Line.Back", null,
            c => b.IsBound ? (b.CanGoBack ? null : "Назад идти некуда") : NotBound, "back"));

        // --- Файлы ---
        registry.Register(Make("file.newfolder", "Новая папка", "Файлы", _ => Run(b.NewFolderCommand), "Line.NewFolder",
            null, NeedConnection, "создать", "каталог", "mkdir"));
        registry.Register(Make("file.newfile", "Новый файл", "Файлы", _ => Run(b.NewFileCommand), "Line.File",
            null, NeedConnection, "создать", "touch"));
        registry.Register(Make("file.upload", "Загрузить файлы", "Файлы", _ => Run(b.UploadCommand), "Line.Up",
            null, NeedConnection, "upload", "отправить", "залить"));
        registry.Register(Make("file.download", "Скачать выбранное", "Файлы", _ => Run(b.DownloadCommand), "Line.Archive",
            null, NeedFileSelection, "download", "сохранить"));
        registry.Register(Make("file.delete", "Удалить выбранное", "Файлы", _ => Run(b.DeleteCommand), "Line.Trash",
            "Del", NeedSelection, "remove", "delete"));
        registry.Register(Make("file.rename", "Переименовать", "Файлы", _ => Run(b.RenameCommand), "Line.FileText",
            "F2", NeedSelection, "имя", "rename"));

        // --- Приложение ---
        registry.Register(Make("app.theme", "Переключить тему", "Приложение", _ => Run(vm.ToggleThemeCommand), "Line.Image",
            null, null, "тёмная", "светлая", "dark", "light", "оформление"));
        registry.Register(Make("app.tunnels", "Туннели", "Приложение", _ => Run(vm.OpenTunnelsCommand), "Line.ChevronRight",
            null, _ => !b.IsBound ? NotBound : vm.IsTunnelsAvailable ? null : "Туннели доступны только для SFTP",
            "проброс", "порты", "ssh", "tunnel", "forward"));

        // --- Плагины ---
        registry.Register(new AppCommand
        {
            Id = "plugin.install",
            Title = "Установить плагин…",
            Category = "Плагины",
            Keywords = new[] { "plugin", "расширение", "checksum" },
            PigIcon = PigIconKind.Snout,
            Handler = async _ =>
            {
                var owner = (dialogs as DialogService)?.Owner;
                if (owner is null || pluginSource.Contains(Sha256Id))
                {
                    if (pluginSource.Contains(Sha256Id))
                        vm.StatusText = "Плагин «Checksum» уже установлен";
                    return;
                }
                if (!await PluginDialogs.ShowInstallAsync(owner, "Checksum"))
                    return;
                // КЛИЕНТСКАЯ ДЕМО-ЗАМЕНА: пока нет бэкенда плагинов (фаза 1), «установка» просто
                // регистрирует встроенную демо-команду. С PluginManager здесь будет запрос на сервер.
                pluginSource.Add(CreateSha256Command(vm, dialogs));
                registry.NotifyChanged();
                vm.StatusText = "Плагин «Checksum» установлен — команда «Посчитать SHA-256» в меню файла";
            },
        });

        registry.AddSource(pluginSource);
        registry.AddSource(new ProfileConnectSource(vm));
    }

    /// <summary>
    /// КЛИЕНТСКАЯ ДЕМО-ЗАМЕНА плагинной команды (до бэкенда плагинов, фаза 1): скачивает файл во временный файл
    /// через API-клиент, считает SHA-256 и показывает хеш. Настоящий плагин сделает это на сервере.
    /// </summary>
    public static AppCommand CreateSha256Command(MainWindowViewModel vm, IDialogService dialogs) => new()
    {
        Id = Sha256Id,
        Title = "Посчитать SHA-256",
        Category = "Плагин: Checksum",
        Keywords = new[] { "sha256", "хеш", "hash", "контрольная сумма", "checksum" },
        Context = AppCommand.FileContext,
        Source = "Checksum (демо, клиент)",
        IconKey = "Line.Code",
        DisabledReason = ctx =>
            !vm.Browser.IsBound ? NotBound
            : (ctx?.Target ?? vm.Browser.SelectedItem) is FileItem { IsDirectory: false } ? null
            : "Выберите файл",
        Handler = async ctx =>
        {
            if ((ctx.Target ?? vm.Browser.SelectedItem) is not FileItem { IsDirectory: false } file)
                return;
            var tmp = Path.GetTempFileName();
            try
            {
                vm.StatusText = $"Считаем SHA-256: {file.Name}…";
                await vm.ApiClient.DownloadFileAsync(vm.Browser.ConnectionId, file.FullPath, tmp);
                await using var stream = File.OpenRead(tmp);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
                vm.StatusText = $"SHA-256 {file.Name}: {hash[..12]}…";
                if ((dialogs as DialogService)?.Owner is { } owner)
                    await PluginDialogs.ShowHashAsync(owner, file.Name, hash);
            }
            catch (Exception ex)
            {
                vm.StatusText = "Не удалось посчитать SHA-256: " + ex.Message;
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* временный файл — лучшее усилие */ }
            }
        },
    };

    private static Task Run(IRelayCommand command, object? parameter = null)
    {
        if (command is IAsyncRelayCommand async)
            return async.ExecuteAsync(parameter);
        command.Execute(parameter);
        return Task.CompletedTask;
    }

    /// <summary>Подрезультаты «Подключиться: prod-01» — по одному на каждый сохранённый профиль.</summary>
    private sealed class ProfileConnectSource(MainWindowViewModel vm) : ICommandSource
    {
        public IEnumerable<AppCommand> GetCommands() => vm.Profiles.Select(p => new AppCommand
        {
            Id = "server.connect." + p.Id,
            Title = ConnectPrefix + p.Name,
            Category = p.UserHost,
            Keywords = new[] { "connect", p.DisplayHost },
            IconKey = "Line.ChevronRight",
            ShowWhenQueryEmpty = false,
            Handler = _ => Run(vm.ConnectProfileCommand, p),
        });
    }
}
