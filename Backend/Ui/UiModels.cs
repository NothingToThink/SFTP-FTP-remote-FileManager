using FileManager.Plugins;

namespace Backend.Ui;

/// <summary>Argument of the client method <c>ShowMessage</c>.</summary>
public record ShowMessageRequest(MessageSeverity Severity, string Message, IReadOnlyList<string> Buttons);

/// <summary>Argument of the client method <c>ShowInputBox</c>.</summary>
public record InputBoxRequest(string Prompt, string? Value = null, string? Placeholder = null, bool Password = false);

/// <summary>A connected UI client.</summary>
public record UiSession(string ConnectionId, DateTimeOffset ConnectedAt);
