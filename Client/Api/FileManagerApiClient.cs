using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FileManagerClient.Models;

namespace FileManagerClient.Api;

/// <summary>
/// Типизированный клиент Backend API (контракт ветки dev @ bdd4928).
///
/// Нюансы контракта:
///  - часть GET/DELETE эндпоинтов принимает путь в теле запроса как JSON-строку;
///  - ответы — camelCase, кроме HostProfile внутри профиля (PascalCase + "$type");
///  - ошибки приходят как {"error": "..."} с кодами 400/404/500.
/// </summary>
public class FileManagerApiClient : IDisposable
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
        => GetWithBodyAsync<FileItem>($"connections/{id}/filesystem/info", path, ct);

    public Task CreateFileAsync(Guid id, string path, CancellationToken ct = default)
        => PostStringAsync($"connections/{id}/filesystem/file", path, ct);

    public Task CreateDirAsync(Guid id, string path, CancellationToken ct = default)
        => PostStringAsync($"connections/{id}/filesystem/dir", path, ct);

    public Task DeleteFileAsync(Guid id, string path, CancellationToken ct = default)
        => DeleteWithBodyAsync($"connections/{id}/filesystem/file", path, ct);

    public Task DeleteDirAsync(Guid id, string path, CancellationToken ct = default)
        => DeleteWithBodyAsync($"connections/{id}/filesystem/dir", path, ct);

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
        => GetWithBodyAsync<long>($"connections/{id}/filesystem/dir/size", path, ct);

    public Task<bool> FileExistsAsync(Guid id, string path, CancellationToken ct = default)
        => GetWithBodyAsync<bool>($"connections/{id}/filesystem/file/exists", path, ct);

    public Task<bool> DirExistsAsync(Guid id, string path, CancellationToken ct = default)
        => GetWithBodyAsync<bool>($"connections/{id}/filesystem/dir/exists", path, ct);

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

    // --- Вспомогательные ---

    private static StringContent JsonBody(object value)
        => new(JsonSerializer.Serialize(value, ClientJson.Options), Encoding.UTF8, "application/json");

    private static StringContent JsonString(string value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct);
        return await ReadAsync<T>(resp, ct);
    }

    private async Task<T> GetWithBodyAsync<T>(string url, string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url) { Content = JsonString(path) };
        using var resp = await _http.SendAsync(req, ct);
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

    private async Task DeleteWithBodyAsync(string url, string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonString(path) };
        using var resp = await _http.SendAsync(req, ct);
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
