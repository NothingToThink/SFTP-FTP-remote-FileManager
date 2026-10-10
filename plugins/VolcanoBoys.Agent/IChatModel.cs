namespace VolcanoBoys.Agent;

/// <summary>The language model behind the agent. Replaced by a fake in tests.</summary>
public interface IChatModel
{
    /// <returns>The text of the answer.</returns>
    /// <exception cref="ChatModelException">The model could not be reached or answered with an error or nothing.</exception>
    Task<string> CompleteAsync(string system, string user, CancellationToken ct);
}

/// <summary>A failure of the model call. The message is safe to show to the user: no key, no bodies of requests.</summary>
public sealed class ChatModelException(string message, Exception? inner = null) : Exception(message, inner);
