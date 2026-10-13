namespace VolcanoBoys.Chat;

public enum ChatRole
{
    User,
    Assistant,
}

public sealed record ChatTurn(ChatRole Role, string Content);

public interface IChatModel
{
    Task<string> CompleteAsync(string system, IReadOnlyList<ChatTurn> history, CancellationToken ct);
}

public sealed class ChatModelException(string message, Exception? inner = null) : Exception(message, inner);
