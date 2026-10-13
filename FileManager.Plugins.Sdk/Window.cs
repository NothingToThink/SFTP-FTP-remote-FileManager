namespace FileManager.Plugins;

public enum MessageSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record InputBoxOptions(
    string Prompt,
    string? Value = null,
    string? Placeholder = null,
    bool Password = false);

/// <summary>Dialogs shown to the user who started the command.</summary>
public interface IWindow
{
    /// <returns>The pressed button, or null if the dialog was dismissed.</returns>
    /// <exception cref="UiUnavailableException">The client is not connected or went away while waiting.</exception>
    Task<string?> ShowMessageAsync(MessageSeverity severity, string message,
        IReadOnlyList<string>? buttons = null, CancellationToken ct = default);

    /// <returns>The entered text, or null if the dialog was dismissed.</returns>
    /// <exception cref="UiUnavailableException">The client is not connected or went away while waiting.</exception>
    Task<string?> ShowInputBoxAsync(InputBoxOptions options, CancellationToken ct = default);
}
