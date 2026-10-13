namespace VolcanoBoys.Agent;

/// <summary>Settings of the agent, read from environment variables. The key is never part of ToString.</summary>
public sealed class AgentSettings
{
    public const string ApiKeyVariable = "FILEMANAGER_AGENT_API_KEY";
    public const string BaseUrlVariable = "FILEMANAGER_AGENT_BASE_URL";
    public const string ModelVariable = "FILEMANAGER_AGENT_MODEL";
    public const string DefaultBaseUrl = "https://api.timeweb.ai/v1";
    public const string DefaultModel = "yandex/yandexgpt-lite";

    private AgentSettings(string apiKey, Uri baseUrl, string model)
    {
        ApiKey = apiKey;
        BaseUrl = baseUrl;
        Model = model;
    }

    public string ApiKey { get; }
    public Uri BaseUrl { get; }
    public string Model { get; }

    /// <returns>The settings, or null with a message for the user (without the key) in <paramref name="error"/>.</returns>
    public static AgentSettings? TryLoad(Func<string, string?> getVariable, out string? error)
    {
        var key = getVariable(ApiKeyVariable)?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            error = $"Не задан ключ API модели. Задайте переменную окружения {ApiKeyVariable} " +
                    "(например, export " + ApiKeyVariable + "=<ключ>) и перезапустите Backend.";
            return null;
        }

        var url = getVariable(BaseUrlVariable)?.Trim();
        if (string.IsNullOrEmpty(url))
            url = DefaultBaseUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme is not ("http" or "https"))
        {
            error = $"{BaseUrlVariable} должна быть адресом http:// или https://.";
            return null;
        }

        var model = getVariable(ModelVariable)?.Trim();
        error = null;
        return new AgentSettings(key, baseUrl, string.IsNullOrEmpty(model) ? DefaultModel : model);
    }

    public override string ToString() => $"AgentSettings {{ BaseUrl = {BaseUrl}, Model = {Model} }}";
}
