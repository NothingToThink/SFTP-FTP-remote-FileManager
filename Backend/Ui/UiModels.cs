namespace Backend.Ui;

public enum MessageSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Argument of the client method <c>ShowMessage</c>.</summary>
public record ShowMessageRequest(MessageSeverity Severity, string Message, IReadOnlyList<string> Buttons);

/// <summary>Argument of the client method <c>ShowInputBox</c>.</summary>
public record InputBoxRequest(string Prompt, string? Value = null, string? Placeholder = null, bool Password = false);

/// <summary>A connected UI client.</summary>
public record UiSession(string ConnectionId, DateTimeOffset ConnectedAt);

/// <summary>The client is not connected, disconnected while we were waiting, or cannot show the dialog.</summary>
public class UiUnavailableException : Exception
{
    public UiUnavailableException(string message) : base(message)
    {
    }

    public UiUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}
