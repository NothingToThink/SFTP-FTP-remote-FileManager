using Backend.Ui;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hubs;

/// <summary>
/// Backend-to-client UI channel. Dialog calls run through <see cref="IUiBridge"/>; chat input is
/// routed from the owning client to its plugin chat session.
/// </summary>
public class UiHub(IUiSessionRegistry registry, IPluginChatSessionRegistry chats) : Hub
{
    public Task SendChatMessage(Guid sessionId, string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > PluginChatSession.MaxMessageLength)
            throw new HubException($"Сообщение должно содержать от 1 до {PluginChatSession.MaxMessageLength} символов.");
        if (!chats.TrySend(Context.ConnectionId, sessionId, message))
            throw new HubException("Чат закрыт или недоступен.");
        return Task.CompletedTask;
    }

    public Task CloseChat(Guid sessionId)
    {
        if (!chats.Close(Context.ConnectionId, sessionId))
            throw new HubException("Чат закрыт или недоступен.");
        return Task.CompletedTask;
    }

    public override Task OnConnectedAsync()
    {
        registry.Add(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        chats.CloseOwner(Context.ConnectionId);
        registry.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
