namespace Backend.Ui;

/// <summary>Asks the user something through a connected UI client.</summary>
public interface IUiBridge
{
    /// <summary>Returns the pressed button's text, or null if the dialog was dismissed.</summary>
    /// <exception cref="UiUnavailableException">Client not connected or disconnected while waiting.</exception>
    Task<string?> ShowMessageAsync(string sessionId, UiSeverity severity, string message,
        IReadOnlyList<string> buttons, CancellationToken ct = default);

    /// <summary>Returns the entered text, or null if the dialog was cancelled.</summary>
    /// <exception cref="UiUnavailableException">Client not connected or disconnected while waiting.</exception>
    Task<string?> ShowInputBoxAsync(string sessionId, InputBoxRequest request, CancellationToken ct = default);
}
