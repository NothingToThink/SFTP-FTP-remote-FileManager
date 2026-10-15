using Avalonia.Controls;
using Avalonia.Input;

namespace FileManagerClient.Services;

/// <summary>Чтение текста из буфера обмена ОС (Avalonia 12: IClipboard → IAsyncDataTransfer). Содержимое не логируется.</summary>
public static class ClipboardText
{
    public static async Task<string?> ReadAsync(TopLevel? top)
    {
        if (top?.Clipboard is not { } clipboard)
            return null;
        using var data = await clipboard.TryGetDataAsync();
        return data is null ? null : await data.TryGetTextAsync();
    }
}
