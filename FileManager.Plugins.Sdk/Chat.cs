namespace FileManager.Plugins;

public sealed record ChatSessionOptions(string Title, string WelcomeMessage);

public enum ChatMessageRole
{
    User,
    Assistant,
}

public sealed record ChatMessage(ChatMessageRole Role, string Content);

/// <summary>Opens persistent chat views in the client for the command's UI session.</summary>
public interface IChatService
{
    Task<IChatSession> OpenAsync(ChatSessionOptions options, CancellationToken ct = default);
}

/// <summary>A chat view that exchanges user and plugin messages until the user closes it.</summary>
public interface IChatSession : IAsyncDisposable
{
    /// <summary>Cancelled when the user closes the chat or its UI client disconnects.</summary>
    CancellationToken ClosedToken { get; }

    /// <returns>The next user message, or null when the user closes the chat.</returns>
    Task<string?> ReadMessageAsync(CancellationToken ct = default);

    Task SendMessageAsync(ChatMessage message, CancellationToken ct = default);
}
