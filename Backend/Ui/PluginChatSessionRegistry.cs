using System.Threading.Channels;
using FileManager.Plugins;

namespace Backend.Ui;

public interface IPluginChatSessionRegistry
{
    PluginChatSession Create(string owner, ChatSessionOptions options,
        Func<Guid, ChatMessage, CancellationToken, Task> sendToClient,
        Func<Guid, CancellationToken, Task> closeClient);
    bool TrySend(string owner, Guid sessionId, string message);
    bool Close(string owner, Guid sessionId);
    void CloseOwner(string owner);
    void Remove(Guid sessionId);
}

public sealed class PluginChatSessionRegistry : IPluginChatSessionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, PluginChatSession> _sessions = [];

    public PluginChatSession Create(string owner, ChatSessionOptions options,
        Func<Guid, ChatMessage, CancellationToken, Task> sendToClient,
        Func<Guid, CancellationToken, Task> closeClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Title) || options.Title.Length > 120)
            throw new ArgumentException("Заголовок чата должен содержать от 1 до 120 символов.", nameof(options));
        ArgumentNullException.ThrowIfNull(options.WelcomeMessage);
        if (options.WelcomeMessage.Length > 4000)
            throw new ArgumentException("Приветствие чата не должно превышать 4000 символов.", nameof(options));

        lock (_gate)
        {
            if (_sessions.Values.Any(session => session.Owner == owner))
                throw new InvalidOperationException("У клиента уже открыт чат с плагином.");

            var session = new PluginChatSession(Guid.NewGuid(), owner, options, this, sendToClient, closeClient);
            _sessions.Add(session.Id, session);
            return session;
        }
    }

    public bool TrySend(string owner, Guid sessionId, string message)
    {
        lock (_gate)
            return _sessions.TryGetValue(sessionId, out var session)
                   && session.Owner == owner
                   && session.TryWrite(message);
    }

    public bool Close(string owner, Guid sessionId)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var session) || session.Owner != owner)
                return false;
            session.CloseFromClient();
            return true;
        }
    }

    public void CloseOwner(string owner)
    {
        lock (_gate)
        {
            foreach (var session in _sessions.Values.Where(session => session.Owner == owner).ToArray())
                session.CloseFromClient();
        }
    }

    public void Remove(Guid sessionId)
    {
        lock (_gate)
            _sessions.Remove(sessionId);
    }
}

public sealed class PluginChatSession : IChatSession
{
    public const int MaxMessageLength = 4000;

    private readonly IPluginChatSessionRegistry _registry;
    private readonly Func<Guid, ChatMessage, CancellationToken, Task> _sendToClient;
    private readonly Func<Guid, CancellationToken, Task> _closeClient;
    private readonly Channel<string> _messages = Channel.CreateBounded<string>(
        new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource _closed = new();
    private readonly CancellationToken _closedToken;
    private int _disposed;

    public PluginChatSession(Guid id, string owner, ChatSessionOptions options, IPluginChatSessionRegistry registry,
        Func<Guid, ChatMessage, CancellationToken, Task> sendToClient,
        Func<Guid, CancellationToken, Task> closeClient)
    {
        Id = id;
        Owner = owner;
        Options = options;
        _registry = registry;
        _sendToClient = sendToClient;
        _closeClient = closeClient;
        _closedToken = _closed.Token;
    }

    public Guid Id { get; }
    public string Owner { get; }
    public ChatSessionOptions Options { get; }
    public CancellationToken ClosedToken => _closedToken;

    public async Task<string?> ReadMessageAsync(CancellationToken ct = default)
    {
        if (_closedToken.IsCancellationRequested)
            return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closedToken);
        try
        {
            return await _messages.Reader.ReadAsync(linked.Token);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
        catch (OperationCanceledException) when (_closedToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public Task SendMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ct.ThrowIfCancellationRequested();
        if (message.Role != ChatMessageRole.Assistant)
            throw new ArgumentException("Плагин может отправлять только сообщения ассистента.", nameof(message));
        ArgumentNullException.ThrowIfNull(message.Content);
        if (message.Content.Length > MaxMessageLength * 2)
            throw new ArgumentException($"Ответ не должен превышать {MaxMessageLength * 2} символов.", nameof(message));
        return _sendToClient(Id, message, ct);
    }

    public bool TryWrite(string message) =>
        message.Length <= MaxMessageLength && _messages.Writer.TryWrite(message);

    public void CloseFromClient()
    {
        if (!_closed.IsCancellationRequested)
            _closed.Cancel();
        _messages.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _registry.Remove(Id);
        CloseFromClient();
        try
        {
            await _closeClient(Id, CancellationToken.None);
        }
        finally
        {
            _closed.Dispose();
        }
    }
}
