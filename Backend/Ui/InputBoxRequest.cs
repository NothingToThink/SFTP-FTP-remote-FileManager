namespace Backend.Ui;

/// <summary>Argument of the client method <c>ShowInputBox</c>.</summary>
public record InputBoxRequest(string Prompt, string? Value = null, string? Placeholder = null, bool Password = false);
