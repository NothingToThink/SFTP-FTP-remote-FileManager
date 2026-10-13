using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Chat;

namespace VolcanoBoys.Agent;

/// <summary>The model through an OpenAI-compatible endpoint (<see cref="AgentSettings.BaseUrl"/>).</summary>
public sealed class OpenAiChatModel : IChatModel
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    private readonly ChatClient _client;
    private readonly TimeSpan _timeout;

    /// <param name="timeout">The whole call, retries included.</param>
    /// <param name="transport">Replaces the HTTP transport; tests use it to avoid the network.</param>
    public OpenAiChatModel(AgentSettings settings, TimeSpan? timeout = null, PipelineTransport? transport = null)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = settings.BaseUrl,
            // A failed call is shown to the user, who asks again; no hidden waiting inside the 90 s.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };
        if (transport is not null)
            options.Transport = transport;
        _client = new ChatClient(model: settings.Model, credential: new ApiKeyCredential(settings.ApiKey), options: options);
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<string> CompleteAsync(string system, string user, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            var response = await _client.CompleteChatAsync(
                [new SystemChatMessage(system), new UserChatMessage(user)], cancellationToken: cts.Token);
            var text = response.Value.Content.Count == 0 ? null : response.Value.Content[0].Text;
            return string.IsNullOrWhiteSpace(text) ? throw new ChatModelException("Модель вернула пустой ответ.") : text;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChatModelException($"Модель не ответила за {_timeout.TotalSeconds:0} с.");
        }
        catch (ClientResultException e)
        {
            // Only the status: the message of the exception may carry the body of the response.
            throw new ChatModelException(e.Status switch
            {
                401 or 403 => $"Модель отклонила ключ API (HTTP {e.Status}). Проверьте {AgentSettings.ApiKeyVariable}.",
                404 => $"Модель или адрес не найдены (HTTP 404). Проверьте {AgentSettings.ModelVariable} и {AgentSettings.BaseUrlVariable}.",
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
