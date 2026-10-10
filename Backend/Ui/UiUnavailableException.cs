namespace Backend.Ui;

/// <summary>
/// The UI client is not connected, or disconnected while we were waiting for its answer.
/// Derives from <see cref="InvalidOperationException"/> so ExceptionMiddleware answers 400 {"error"}.
/// </summary>
public class UiUnavailableException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);
