using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileManagerClient.Api;
using FileManagerClient.ViewModels;
using FileManagerClient.Views.Dialogs;

namespace FileManagerClient.Services;

/// <summary>
/// Модальные диалоги и файловые пикеры. Интерфейс — для подмены в тестах
/// (headless-режим не умеет ShowDialog, автотесты не нажимают кнопки).
/// </summary>
public interface IDialogService
{
    Task<string?> PromptAsync(string title, string label, string defaultValue = "");
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowMessageAsync(string title, string message);
    Task<string?> PickOpenFileAsync(string title);
    Task<string?> PickSaveFileAsync(string suggestedName);
    Task<bool> ShowProfileEditorAsync(ProfileEditViewModel viewModel);

    // --- Диалоги волны 2D. Реализации по умолчанию — для заглушек в тестах/DevAudit (ничего не показывают). ---

    /// <summary>Показать ошибку (свинка, текст, «Подробнее», кнопки). Результат — нажатое действие или null (Esc).</summary>
    Task<UserErrorAction?> ShowErrorAsync(UserError error) => Task.FromResult<UserErrorAction?>(null);

    /// <summary>«Знакомимся с сервером»: true — пользователь доверяет отпечатку.</summary>
    Task<bool> ShowUnknownHostKeyAsync(string host, int port, string fingerprint) => Task.FromResult(false);

    /// <summary>«Чужой запах!»: true — пользователь осознанно доверяет новому ключу (по умолчанию — отказ).</summary>
    Task<bool> ShowHostKeyMismatchAsync(string host, int port, string? expected, string? presented) => Task.FromResult(false);

    /// <summary>«Удалить 3 файла с сервера?» / «Это нельзя отменить.» Для одного элемента передайте его имя.</summary>
    Task<bool> ConfirmDeleteAsync(int count, string? singleName = null, bool hasFolders = false) => Task.FromResult(false);

    /// <summary>«Заменить файл?» — на сервере уже есть элемент с таким именем.</summary>
    Task<bool> ConfirmOverwriteAsync(string name) => Task.FromResult(false);

    /// <summary>Немодальное окно туннелей (живёт рядом с главным окном).</summary>
    void ShowTunnelsWindow(TunnelsViewModel viewModel);

    /// <summary>Модальный диалог создания туннеля: true — подтверждено.</summary>
    Task<bool> ShowCreateForwardDialogAsync(CreateForwardViewModel viewModel, string connectionName,
        IForwardingApi api, Guid connectionId);
}

/// <summary>Реализация на настоящих окнах; владелец задаётся при открытии главного окна.</summary>
public class DialogService : IDialogService
{
    public Window? Owner { get; set; }

    /// <summary>
    /// Адрес бэкенда для кнопки «Проверить подключение» в редакторе профиля.
    /// Если не задан — берётся из сохранённых настроек клиента.
    /// </summary>
    public Func<string>? ServerUrlProvider { get; set; }

    public async Task<string?> PromptAsync(string title, string label, string defaultValue = "")
    {
        if (Owner is null)
            return null;

        var input = new TextBox
        {
            Text = defaultValue,
            PlaceholderText = label,
            MinWidth = 360,
        };

        var (dialog, buttons) = BuildDialog(title, label, input);
        string? result = null;

        var ok = new Button { Content = "OK", Width = 90, IsDefault = true };
        var cancel = new Button { Content = "Отмена", Width = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            result = input.Text;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        AddButtons((dialog, buttons), ok, cancel);
        await dialog.ShowDialog<string?>(Owner);
        return result;
    }

    public Task<bool> ConfirmAsync(string title, string message)
        => ShowYesNoAsync(title, message, "Да", "Нет");

    public async Task ShowMessageAsync(string title, string message)
    {
        if (Owner is null)
            return;

        var (dialog, buttons) = BuildDialog(title, message, content: null);

        var ok = new Button { Content = "OK", Width = 90, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => dialog.Close();

        AddButtons((dialog, buttons), ok);
        await dialog.ShowDialog<object?>(Owner);
    }

    private async Task<bool> ShowYesNoAsync(string title, string message, string yes, string no)
    {
        if (Owner is null)
            return false;

        var (dialog, buttons) = BuildDialog(title, message, content: null);
        var result = false;

        var yesButton = new Button { Content = yes, Width = 90, IsDefault = true };
        var noButton = new Button { Content = no, Width = 90, IsCancel = true };
        yesButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        noButton.Click += (_, _) => dialog.Close();

        AddButtons((dialog, buttons), yesButton, noButton);
        await dialog.ShowDialog<object?>(Owner);
        return result;
    }

    public async Task<string?> PickOpenFileAsync(string title)
    {
        if (Owner is null)
            return null;

        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName)
    {
        if (Owner is null)
            return null;

        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<bool> ShowProfileEditorAsync(ProfileEditViewModel viewModel)
    {
        if (Owner is null)
            return false;

        viewModel.Tester ??= new ApiConnectionTester(ServerUrlProvider ?? (() => ClientSettings.Load().ServerUrl));
        var window = new Views.ProfileEditWindow
        {
            DataContext = viewModel,
        };
        return await window.ShowDialog<bool>(Owner);
    }

    public async Task<UserErrorAction?> ShowErrorAsync(UserError error)
    {
        if (Owner is null)
            return null;
        return await new ErrorDialog(error).ShowDialog<UserErrorAction?>(Owner);
    }

    public async Task<bool> ShowUnknownHostKeyAsync(string host, int port, string fingerprint)
    {
        if (Owner is null)
            return false;
        return await HostKeyDialog.FirstMeet(host, port, fingerprint).ShowDialog<bool>(Owner);
    }

    public async Task<bool> ShowHostKeyMismatchAsync(string host, int port, string? expected, string? presented)
    {
        if (Owner is null)
            return false;
        return await HostKeyDialog.Mismatch(host, port, expected, presented).ShowDialog<bool>(Owner);
    }

    public async Task<bool> ConfirmDeleteAsync(int count, string? singleName = null, bool hasFolders = false)
    {
        if (Owner is null)
            return false;
        var (title, text) = DeleteTexts(count, singleName, hasFolders);
        return await new ConfirmDialog(title, text, "Удалить", danger: true).ShowDialog<bool>(Owner);
    }

    public async Task<bool> ConfirmOverwriteAsync(string name)
    {
        if (Owner is null)
            return false;
        var (title, text) = OverwriteTexts(name);
        return await new ConfirmDialog(title, text, "Заменить", danger: true).ShowDialog<bool>(Owner);
    }

    /// <summary>Тексты подтверждения удаления (без шуток, по таблице спеки). Публично для тестов и скриншотов.</summary>
    public static (string Title, string Text) DeleteTexts(int count, string? singleName, bool hasFolders = false)
    {
        var title = count == 1 && !string.IsNullOrEmpty(singleName)
            ? $"Удалить «{singleName}» с сервера?"
            : $"Удалить {count} {RuPlural(count, "файл", "файла", "файлов")} с сервера?";
        var text = hasFolders ? "Папки будут удалены вместе с содержимым. Это нельзя отменить." : "Это нельзя отменить.";
        return (title, text);
    }

    public static (string Title, string Text) OverwriteTexts(string name)
        => ($"Заменить «{name}»?", "На сервере уже есть элемент с таким именем. Старое содержимое будет потеряно.");

    /// <summary>Склонение по числу: 1 файл, 2 файла, 5 файлов, 21 файл.</summary>
    public static string RuPlural(int n, string one, string few, string many)
    {
        n = Math.Abs(n);
        if (n % 100 is >= 11 and <= 14) return many;
        return (n % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }

    public void ShowTunnelsWindow(TunnelsViewModel viewModel)
    {
        if (Owner is null)
            return;

        new Views.TunnelsWindow { DataContext = viewModel }.Show(Owner);
    }

    public async Task<bool> ShowCreateForwardDialogAsync(CreateForwardViewModel viewModel,
        string connectionName, IForwardingApi api, Guid connectionId)
    {
        if (Owner is null)
            return false;

        var window = new Views.CreateForwardWindow(api, connectionId)
        {
            DataContext = viewModel,
            Title = $"Новый туннель — {connectionName}",
        };
        return await window.ShowDialog<bool>(Owner);
    }

    private static (Window Dialog, StackPanel Buttons) BuildDialog(string title, string message, Control? content)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            MaxWidth = 560,
        };

        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
        });

        if (content is not null)
            panel.Children.Add(content);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        return (dialog, buttons);
    }

    private static void AddButtons((Window Dialog, StackPanel Buttons) dialog, params Button[] buttons)
    {
        foreach (var button in buttons)
            dialog.Buttons.Children.Add(button);
    }
}
