using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Chat;

namespace VolcanoBoys.Chat;

public sealed class OpenAiChatModel : IChatModel
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    private readonly ChatClient _client;
    private readonly TimeSpan _timeout;

    public OpenAiChatModel(ChatSettings settings, TimeSpan? timeout = null, PipelineTransport? transport = null)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = settings.BaseUrl,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };
        if (transport is not null)
            options.Transport = transport;
        _client = new ChatClient(model: settings.Model, credential: new ApiKeyCredential(settings.ApiKey), options: options);
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<string> CompleteAsync(string system, IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        var messages = new List<ChatMessage>(history.Count + 1) { new SystemChatMessage(system) };
        messages.AddRange(history.Select(turn => turn.Role switch
        {
            ChatRole.User => (ChatMessage)new UserChatMessage(turn.Content),
            ChatRole.Assistant => new AssistantChatMessage(turn.Content),
            _ => throw new ArgumentOutOfRangeException(nameof(history), "Неизвестная роль сообщения."),
        }));

        try
        {
            var response = await _client.CompleteChatAsync(messages, cancellationToken: cts.Token);
            var text = string.Concat(response.Value.Content.Select(part => part.Text));
            return string.IsNullOrWhiteSpace(text)
                ? throw new ChatModelException("Модель вернула пустой ответ.")
                : text;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChatModelException($"Модель не ответила за {_timeout.TotalSeconds:0} с.");
        }
        catch (ClientResultException e)
        {
            throw new ChatModelException(e.Status switch
            {
                401 or 403 => $"Модель отклонила ключ API (HTTP {e.Status}). Проверьте {ChatSettings.ApiKeyVariable}.",
                404 => $"Модель или адрес не найдены (HTTP 404). Проверьте {ChatSettings.ModelVariable} и {ChatSettings.BaseUrlVariable}.",
                429 => "Модель отказала из-за лимита запросов (HTTP 429). Попробуйте позже.",
                0 => "Не удалось связаться с моделью. Проверьте сеть и адрес сервиса.",
                _ => $"Модель вернула ошибку (HTTP {e.Status}).",
            });
        }
        catch (HttpRequestException)
        {
            throw new ChatModelException("Не удалось связаться с моделью. Проверьте сеть и адрес сервиса.");
        }
    }
}
