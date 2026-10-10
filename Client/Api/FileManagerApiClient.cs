using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FileManagerClient.Models;

namespace FileManagerClient.Api;

/// <summary>
/// Операции порт-форвардинга соединения (только SFTP/SSH). Выделен интерфейсом,
/// чтобы тесты подменяли транспорт (по духу IDialogService).
/// </summary>
public interface IForwardingApi
{
    Task<List<ForwardStatus>> GetForwardsAsync(Guid id, CancellationToken ct = default);
    Task<ForwardStatus> CreateForwardAsync(Guid id, ForwardType type, int bindPort,
        string? name = null, string? bindHost = null, string? targetHost = null,
        int? targetPort = null, CancellationToken ct = default);
    Task<ForwardStatus?> GetForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default);
    Task<ForwardStatus> StopForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default);
    Task<ForwardStatus> RestartForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default);
    Task DeleteForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default);
    Task<List<PortSuggestion>> GetForwardSuggestionsAsync(Guid id, string? text = null,
        int? portMin = null, int? portMax = null, bool loopbackOnly = false, int limit = 50,
        CancellationToken ct = default);
}

/// <summary>
/// Типизированный клиент Backend API (контракт ветки dev @ bdd4928).
///
/// Нюансы контракта:
///  - часть GET/DELETE эндпоинтов принимает путь в теле запроса как JSON-строку;
///  - ответы — camelCase, кроме HostProfile внутри профиля (PascalCase + "$type");
///  - ошибки приходят как {"error": "..."} с кодами 400/404/500.
/// </summary>
public class FileManagerApiClient : IForwardingApi, IDisposable
{
    private readonly HttpClient _http;

    public string BaseUrl { get; }

    public FileManagerApiClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromMinutes(10),
        };
    }

    public void Dispose() => _http.Dispose();

    // --- Профили ---

    public Task<List<Guid>> GetProfileIdsAsync(CancellationToken ct = default)
        => GetAsync<List<Guid>>("profiles", ct);

    public Task<SavedProfile> GetProfileAsync(Guid id, CancellationToken ct = default)
        => GetAsync<SavedProfile>($"profiles/{id}", ct);

    public Task<Guid> SaveProfileAsync(SavedProfile profile, CancellationToken ct = default)
        => PostAsync<Guid>("profiles", profile, ct);

    public Task DeleteProfileAsync(Guid id, CancellationToken ct = default)
        => DeleteAsync($"profiles/{id}", ct);

    // --- Соединения ---

    public Task<List<Guid>> GetConnectionIdsAsync(CancellationToken ct = default)
        => GetAsync<List<Guid>>("connections", ct);

    public Task<Guid> CreateConnectionAsync(SavedProfile profile, CancellationToken ct = default)
        => PostAsync<Guid>("connections", profile, ct);

    public Task DeleteConnectionAsync(Guid id, CancellationToken ct = default)
        => DeleteAsync($"connections/{id}", ct);

    public Task ConnectAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"connections/{id}/connect", ct);

    public Task DisconnectAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"connections/{id}/disconnect", ct);

    public Task<bool> GetConnectionStateAsync(Guid id, CancellationToken ct = default)
        => GetAsync<bool>($"connections/{id}/state", ct);

    // --- Файловая система соединения ---

    public Task<List<FileItem>> GetFilesAsync(Guid id, CancellationToken ct = default)
        => GetAsync<List<FileItem>>($"connections/{id}/filesystem", ct);

    public Task<FileItem> GetFileInfoAsync(Guid id, string path, CancellationToken ct = default)
        => GetAsync<FileItem>(WithPath($"connections/{id}/filesystem/info", path), ct);

    public Task CreateFileAsync(Guid id, string path, CancellationToken ct = default)
        => PostStringAsync($"connections/{id}/filesystem/file", path, ct);

    public Task CreateDirAsync(Guid id, string path, CancellationToken ct = default)
        => PostStringAsync($"connections/{id}/filesystem/dir", path, ct);

    public Task DeleteFileAsync(Guid id, string path, CancellationToken ct = default)
        => DeleteAsync(WithPath($"connections/{id}/filesystem/file", path), ct);

    public Task DeleteDirAsync(Guid id, string path, CancellationToken ct = default)
        => DeleteAsync(WithPath($"connections/{id}/filesystem/dir", path), ct);

    public Task RenameFileAsync(Guid id, string oldPath, string newPath, CancellationToken ct = default)
        => PatchAsync($"connections/{id}/filesystem/file", new { oldPath, newPath }, ct);

    public Task RenameDirAsync(Guid id, string oldPath, string newPath, CancellationToken ct = default)
        => PatchAsync($"connections/{id}/filesystem/dir", new { oldPath, newPath }, ct);

    public Task CopyFileAsync(Guid id, string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
        => PostAsync($"connections/{id}/filesystem/file/copy", new { sourcePath, targetPath, canOverride }, ct);

    public Task MoveFileAsync(Guid id, string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
        => PatchAsync($"connections/{id}/filesystem/file/move", new { sourcePath, targetPath, canOverride }, ct);

    public Task CopyDirAsync(Guid id, string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
        => PostAsync($"connections/{id}/filesystem/dir/copy", new { sourcePath, targetPath, canOverride }, ct);

    public Task MoveDirAsync(Guid id, string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
        => PatchAsync($"connections/{id}/filesystem/dir/move", new { sourcePath, targetPath, canOverride }, ct);

    public Task<long> GetDirSizeAsync(Guid id, string path, CancellationToken ct = default)
        => GetAsync<long>(WithPath($"connections/{id}/filesystem/dir/size", path), ct);

    public Task<bool> FileExistsAsync(Guid id, string path, CancellationToken ct = default)
        => GetAsync<bool>(WithPath($"connections/{id}/filesystem/file/exists", path), ct);

    public Task<bool> DirExistsAsync(Guid id, string path, CancellationToken ct = default)
        => GetAsync<bool>(WithPath($"connections/{id}/filesystem/dir/exists", path), ct);

    // --- Порт-форвардинг (только SFTP/SSH соединения) ---

    public Task<List<ForwardStatus>> GetForwardsAsync(Guid id, CancellationToken ct = default)
        => GetAsync<List<ForwardStatus>>($"connections/{id}/forwards", ct);

    public Task<ForwardStatus> CreateForwardAsync(Guid id, ForwardType type, int bindPort,
        string? name = null, string? bindHost = null, string? targetHost = null,
        int? targetPort = null, CancellationToken ct = default)
        => PostAsync<ForwardStatus>($"connections/{id}/forwards",
            new { type, bindPort, name, bindHost, targetHost, targetPort }, ct);

    public async Task<ForwardStatus?> GetForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default)
    {
        try
        {
            return await GetAsync<ForwardStatus>($"connections/{id}/forwards/{ruleId}", ct);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public Task<ForwardStatus> StopForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default)
        => PostAsync<ForwardStatus>($"connections/{id}/forwards/{ruleId}/stop", new { }, ct);

    public Task<ForwardStatus> RestartForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default)
        => PostAsync<ForwardStatus>($"connections/{id}/forwards/{ruleId}/start", new { }, ct);

    public async Task DeleteForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default)
    {
        // сервер отвечает 204 NoContent
        using var resp = await _http.DeleteAsync($"connections/{id}/forwards/{ruleId}", ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public Task<List<PortSuggestion>> GetForwardSuggestionsAsync(Guid id, string? text = null,
        int? portMin = null, int? portMax = null, bool loopbackOnly = false, int limit = 50,
        CancellationToken ct = default)
    {
        var query = $"connections/{id}/forwards/suggestions?loopbackOnly={loopbackOnly.ToString().ToLower()}&limit={limit}";
        if (!string.IsNullOrEmpty(text))
            query += $"&text={Uri.EscapeDataString(text)}";
        if (portMin is not null)
            query += $"&portMin={portMin}";
        if (portMax is not null)
            query += $"&portMax={portMax}";
        return GetAsync<List<PortSuggestion>>(query, ct);
    }

    /// <summary>
    /// MVC отдаёт string-результаты без JSON-обёртки (StringOutputFormatter: text/plain,
    /// без кавычек), поэтому читаем тело как текст и при необходимости снимаем JSON-кавычки.
    /// </summary>
    public async Task<string> GetWorkingDirectoryAsync(Guid id, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"connections/{id}/filesystem/dir/current", ct);
        await EnsureSuccessAsync(resp, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (body.Length == 0)
            return string.Empty;
        return body.StartsWith('"')
            ? JsonSerializer.Deserialize<string>(body, ClientJson.Options) ?? string.Empty
            : body;
    }

    public Task ChangeDirectoryAsync(Guid id, string path, CancellationToken ct = default)
        => PatchStringAsync($"connections/{id}/filesystem/dir/current", path, ct);

    public async Task UploadFileAsync(Guid id, string remotePath, string localFilePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(localFilePath);
        using var form = new MultipartFormDataContent();
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", Path.GetFileName(localFilePath));

        using var resp = await _http.PostAsync(
            $"connections/{id}/filesystem/file/upload?remotePath={Uri.EscapeDataString(remotePath)}",
            form,
            ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task DownloadFileAsync(Guid id, string remotePath, string localFilePath, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(
            $"connections/{id}/filesystem/file/download?path={Uri.EscapeDataString(remotePath)}",
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        await EnsureSuccessAsync(resp, ct);

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(localFilePath);
        await src.CopyToAsync(dst, ct);
    }

    // --- Команды плагинов ---

    public Task<List<PluginCommand>> GetCommandsAsync(CancellationToken ct = default)
        => GetAsync<List<PluginCommand>>("commands", ct);

    /// <summary>
    /// Запускает команду плагина. Ответ 202 — результата не ждём: диалоги плагин откроет сам
    /// через хаб /hubs/ui клиента с ConnectionId = <paramref name="uiSessionId"/>.
    /// </summary>
    public async Task ExecuteCommandAsync(string commandId, string uiSessionId, Guid? connectionId,
        string? currentPath, IReadOnlyList<string> selectedPaths, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"commands/{Uri.EscapeDataString(commandId)}/execute")
        {
            Content = JsonBody(new { connectionId, currentPath, selectedPaths }),
        };
        req.Headers.Add("X-UI-Session", uiSessionId);
        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    // --- Вспомогательные ---

    private static StringContent JsonBody(object value)
        => new(JsonSerializer.Serialize(value, ClientJson.Options), Encoding.UTF8, "application/json");

    // Путь в query кодируется только здесь: Uri.EscapeDataString превращает '+' в %2B,
    // иначе ASP.NET Core прочитает его как пробел.
    private static string WithPath(string url, string path)
        => $"{url}?path={Uri.EscapeDataString(path)}";

    private static StringContent JsonString(string value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct);
        return await ReadAsync<T>(resp, ct);
    }

    private async Task<T> PostAsync<T>(string url, object body, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(url, JsonBody(body), ct);
        return await ReadAsync<T>(resp, ct);
    }

    private async Task PostAsync(string url, object body, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(url, JsonBody(body), ct);
        await EnsureSuccessAsync(resp, ct);
    }

    private async Task PostAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(url, content: null, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    private async Task PostStringAsync(string url, string path, CancellationToken ct)
    {
        using var content = JsonString(path);
        using var resp = await _http.PostAsync(url, content, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    private async Task PatchAsync(string url, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonBody(body) };
        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    private async Task PatchStringAsync(string url, string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonString(path) };
        using var resp = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    private async Task DeleteAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.DeleteAsync(url, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        await EnsureSuccessAsync(resp, ct);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, ClientJson.Options)
               ?? throw new ApiException(500, "Пустой ответ сервера");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode)
            return;

        string message;
        try
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            try
            {
                using var doc = JsonDocument.Parse(body);
                message = doc.RootElement.TryGetProperty("error", out var err)
                    ? err.GetString() ?? body
                    : body;
            }
            catch (JsonException)
            {
                message = string.IsNullOrWhiteSpace(body) ? resp.ReasonPhrase ?? "нет описания" : body;
            }
        }
        catch
        {
            message = resp.ReasonPhrase ?? "нет описания";
        }

        throw new ApiException((int)resp.StatusCode, message);
    }
}
