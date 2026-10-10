namespace Backend.Ui;

/// <summary>Argument of the client method <c>ShowMessage</c>.</summary>
public record UiMessageRequest(UiSeverity Severity, string Message, string[] Buttons);
