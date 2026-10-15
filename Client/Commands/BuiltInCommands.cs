using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Controls;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;

namespace FileManagerClient.Commands;

/// <summary>Встроенные команды, привязанные к существующим командам MainWindowViewModel и FileBrowserViewModel.</summary>
public static class BuiltInCommands
{
    public const string ConnectPrefix = "Подключиться: ";

    private const string NotBound = "Сначала подключитесь к серверу";

    public static void Register(CommandRegistry registry, MainWindowViewModel vm, IDialogService dialogs)
    {
        var b = vm.Browser;

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
        registry.Register(Make("app.sync", "Синхронизация профилей…", "Приложение", _ => Run(vm.OpenSyncCommand),
            "Line.Refresh", null, null, "sync", "облако", "сервер синхронизации", "отправить профили", "забрать профили", "войти"));
        registry.Register(Make("app.tunnels", "Туннели", "Приложение", _ => Run(vm.OpenTunnelsCommand), "Line.ChevronRight",
            null, _ => !b.IsBound ? NotBound : vm.IsTunnelsAvailable ? null : "Туннели доступны только для SFTP",
            "проброс", "порты", "ssh", "tunnel", "forward"));

        // --- Плагины ---
        // Установки плагина нет ни в Backend, ни в API: плагин кладут в папку плагинов и перезапускают сервер.
        registry.Register(Make("plugin.openfolder", "Открыть папку плагинов", "Плагины", _ =>
        {
            var folder = PluginsFolder();
            try { Directory.CreateDirectory(folder); } catch { /* ошибку покажет FolderOpener */ }
            vm.StatusText = FolderOpener.Open(folder) ? "Открыта папка плагинов: " + folder : "Не удалось открыть папку: " + folder;
            return Task.CompletedTask;
        }, "Line.Archive", null, null, "plugin", "плагин", "расширение", "папка"));
        registry.Register(Make("plugin.refresh", "Обновить список плагинов", "Плагины", async _ =>
        {
            var changed = await vm.PluginSource.RefreshAsync();
            vm.StatusText = changed ? $"Команд плагинов: {vm.PluginSource.Commands.Count}" : "Список плагинов не изменился";
        }, "Line.Refresh", null, null, "plugin", "плагин", "перечитать", "refresh"));

        registry.AddSource(new ProfileConnectSource(vm));
    }

    /// <summary>Папка плагинов Backend: &lt;данные сервера&gt;/RemoteFileManager/plugins (правило Core.Utils.AppPaths).</summary>
    public static string PluginsFolder()
    {
        var basePath = Environment.GetEnvironmentVariable("REMOTE_FILE_MANAGER_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(basePath, "RemoteFileManager", "plugins");
    }

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
