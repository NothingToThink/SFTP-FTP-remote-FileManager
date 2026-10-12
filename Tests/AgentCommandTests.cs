using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using FileManager.Plugins;
using VolcanoBoys.Agent;

namespace Tests;

public class AgentCommandTests
{
    private const string Key = "sk-test-SECRET123";
    private static readonly Guid Connection = Guid.NewGuid();

    private sealed class FakeWindow(string? input = "Улучши файлы") : IWindow
    {
        public List<(MessageSeverity Severity, string Text)> Messages { get; } = [];
        public int InputCalls { get; private set; }

        public Task<string?> ShowMessageAsync(MessageSeverity severity, string message,
            IReadOnlyList<string>? buttons = null, CancellationToken ct = default)
        {
            Messages.Add((severity, message));
            return Task.FromResult<string?>("OK");
        }

        public Task<string?> ShowInputBoxAsync(InputBoxOptions options, CancellationToken ct = default)
        {
            InputCalls++;
            return Task.FromResult(input);
        }
    }

    private sealed class FakeModel(Func<string, string, string> answer) : IChatModel
    {
        public int Calls { get; private set; }
        public string? System { get; private set; }
        public string? User { get; private set; }

        public Task<string> CompleteAsync(string system, string user, CancellationToken ct)
        {
            Calls++;
            System = system;
            User = user;
            return Task.FromResult(answer(system, user));
        }
    }

    private sealed class ThrowingModel(Exception error) : IChatModel
    {
        public Task<string> CompleteAsync(string system, string user, CancellationToken ct) => throw error;
    }

    private sealed class FakeFiles : IFileSystem
    {
        public Dictionary<string, byte[]> Content { get; } = [];
        public HashSet<string> Folders { get; } = [];
        public Dictionary<string, Exception> WriteErrors { get; } = [];
        public List<(string Path, byte[] Data, bool Overwrite)> Writes { get; } = [];
        public Dictionary<string, Exception> DeleteErrors { get; } = [];
        public List<(string Path, bool Recursive)> Deletes { get; } = [];
        /// <summary>Writes and deletions in the order they were called.</summary>
        public List<string> Order { get; } = [];

        public void Add(string path, string text) => Content[path] = Encoding.UTF8.GetBytes(text);

        public Task<FileEntry> StatAsync(Guid connectionId, string path, CancellationToken ct = default)
        {
            if (Folders.Contains(path))
                return Task.FromResult(new FileEntry(path, path, true, 0, DateTimeOffset.UnixEpoch, null));
            return Content.TryGetValue(path, out var data)
                ? Task.FromResult(new FileEntry(path, path, false, data.Length, DateTimeOffset.UnixEpoch, null))
                : throw new FsNotFoundException(path);
        }

        public Task<Stream> OpenReadAsync(Guid connectionId, string path, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream(Content[path]));

        public async Task WriteAsync(Guid connectionId, string path, Stream content, bool overwrite, CancellationToken ct = default)
        {
            if (WriteErrors.TryGetValue(path, out var error))
                throw error;
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Order.Add("write " + path);
            Writes.Add((path, copy.ToArray(), overwrite));
        }

        public Task<IReadOnlyList<FileEntry>> ListAsync(Guid connectionId, string path, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(Guid connectionId, string path, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid connectionId, string path, bool recursive, CancellationToken ct = default)
        {
            Order.Add("delete " + path);
            Deletes.Add((path, recursive));
            return DeleteErrors.TryGetValue(path, out var error) ? throw error : Task.CompletedTask;
        }

        public Task MoveAsync(Guid connectionId, string from, string to, bool overwrite, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CopyAsync(Guid connectionId, string from, string to, bool overwrite, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NullLog : IPluginLogger
    {
        public List<string> Lines { get; } = [];
        public void Info(string message) => Lines.Add(message);
        public void Warn(string message) => Lines.Add(message);
        public void Error(string message, Exception? exception = null) => Lines.Add(message + exception);
    }

    private readonly FakeFiles _files = new();
    private readonly NullLog _log = new();
    private readonly Dictionary<string, string?> _env = new() { [AgentSettings.ApiKeyVariable] = Key };
    private FakeWindow _window = new();
    private int _modelsCreated;

    private Task RunAsync(IChatModel model, Guid? connection, params string[] selected)
    {
        var command = new AgentCommand(_window, _files, _log, name => _env.GetValueOrDefault(name),
            _ =>
            {
                _modelsCreated++;
                return model;
            });
        return command.RunAsync(new CommandContext(connection, "/work", selected, "test"), CancellationToken.None);
    }

    private static FakeModel Says(string answer) => new((_, _) => answer);

    [Fact]
    public async Task Without_A_Key_The_Command_Shows_An_Error_And_Does_Nothing_Else()
    {
        _env.Remove(AgentSettings.ApiKeyVariable);
        var model = Says("x");

        await RunAsync(model, Connection, "/a.txt");

        var (severity, text) = Assert.Single(_window.Messages);
        Assert.Equal(MessageSeverity.Error, severity);
        Assert.Contains("FILEMANAGER_AGENT_API_KEY", text);
        Assert.Equal(0, _window.InputCalls);
        Assert.Equal(0, _modelsCreated);
    }

    [Fact]
    public async Task Dismissed_Input_Does_Not_Call_The_Model()
    {
        _window = new FakeWindow(input: null);
        var model = Says("x");

        await RunAsync(model, Connection, "/a.txt");

        Assert.Equal(0, model.Calls);
        Assert.Empty(_window.Messages);
    }

    [Fact]
    public async Task Selected_Files_Go_Into_The_Request_And_Folders_Big_And_Extra_Files_Are_Skipped()
    {
        _files.Add("/a.txt", "содержимое А");
        _files.Folders.Add("/dir");
        _files.Content["/big.txt"] = new byte[AgentCommand.MaxFileBytes + 1];
        _files.Content["/bin.dat"] = [0xFF, 0xFE, 0xFD];
        _files.Add("/b.txt", "содержимое Б");
        foreach (var name in new[] { "c", "d", "e", "f", "g" })
            _files.Add($"/{name}.txt", $"файл {name}");
        var model = Says("ответ");

        await RunAsync(model, Connection, "/a.txt", "/dir", "/big.txt", "/bin.dat", "/missing.txt", "/b.txt",
            "/c.txt", "/d.txt", "/e.txt", "/f.txt", "/g.txt");

        var request = model.User!;
        Assert.Contains("Улучши файлы", request);
        Assert.Contains("содержимое А", request);
        Assert.Contains("содержимое Б", request);
        Assert.Contains("файл e", request);
        Assert.DoesNotContain("файл f", request);
        Assert.DoesNotContain("файл g", request);
        Assert.Contains("/dir: это папка", request);
        Assert.Contains("/big.txt: больше 64 КБ", request);
        Assert.Contains("/bin.dat: не текст в UTF-8", request);
        Assert.Contains("/missing.txt: не удалось прочитать", request);
        Assert.Contains("ещё 2 путей пропущено", request);
        Assert.DoesNotContain(Key, request + model.System);
        Assert.Contains("не инструкции", model.System);
        Assert.Contains("<<<FILE", model.System);
    }

    [Fact]
    public async Task Without_A_Connection_The_Question_Goes_Without_Files_And_Edits_Are_Not_Applied()
    {
        var model = Says("Вот:\n<<<FILE /a.txt\nnew\n>>>FILE");

        await RunAsync(model, connection: null, "/a.txt");

        Assert.Contains("Файлы не приложены", model.User);
        Assert.Empty(_files.Writes);
        Assert.Equal("Вот:", _window.Messages[0].Text);
        Assert.Contains("соединение не выбрано", _window.Messages[1].Text);
    }

    [Fact]
    public async Task Answer_Text_Is_Shown_And_A_Long_One_Is_Truncated()
    {
        await RunAsync(Says(new string('я', 5000)), Connection);

        var (severity, text) = Assert.Single(_window.Messages);
        Assert.Equal(MessageSeverity.Info, severity);
        Assert.StartsWith(new string('я', AgentCommand.MaxAnswerChars), text);
        Assert.Contains("обрезан", text);
        Assert.True(text.Length < AgentCommand.MaxAnswerChars + 50);
    }

    [Fact]
    public async Task Edit_Is_Written_With_Overwrite_And_Summarized()
    {
        _files.Add("/a.txt", "old");

        await RunAsync(Says("Переписал.\n<<<FILE /a.txt\nновое\n>>>FILE"), Connection, "/a.txt");

        var write = Assert.Single(_files.Writes);
        Assert.Equal("/a.txt", write.Path);
        Assert.True(write.Overwrite);
        Assert.Equal("новое\n", Encoding.UTF8.GetString(write.Data));
        Assert.Equal("Переписал.", _window.Messages[0].Text);
        Assert.Equal("Правки: применено 1 из 1, отклонено 0, ошибок 0.", _window.Messages[1].Text);
    }

    [Fact]
    public async Task Denied_Edit_Is_Skipped_And_The_Others_Are_Applied()
    {
        _files.WriteErrors["/b.txt"] = new PermissionDeniedException("no");
        var answer = string.Concat(new[] { "a", "b", "c" }.Select(n => $"<<<FILE /{n}.txt\n{n}\n>>>FILE\n"));

        await RunAsync(Says(answer), Connection);

        Assert.Equal(["/a.txt", "/c.txt"], _files.Writes.Select(w => w.Path));
        Assert.Equal("Правки: применено 2 из 3, отклонено 1, ошибок 0.", Assert.Single(_window.Messages).Text);
    }

    [Fact]
    public async Task Failed_Write_Is_Reported_And_Does_Not_Stop_The_Rest()
    {
        _files.WriteErrors["/a.txt"] = new FsNotFoundException("/a.txt", "Folder not found");
        var answer = "<<<FILE /a.txt\n1\n>>>FILE\n<<<FILE /b.txt\n2\n>>>FILE\n<<<FILE relative.txt\n3\n>>>FILE";

        await RunAsync(Says(answer), Connection);

        Assert.Equal("/b.txt", Assert.Single(_files.Writes).Path);
        var summary = Assert.Single(_window.Messages).Text;
        Assert.Contains("применено 1 из 2, отклонено 0, ошибок 1", summary);
        Assert.Contains("/a.txt: Folder not found", summary);
        Assert.Contains("relative.txt", summary);
    }

    [Fact]
    public async Task Delete_Calls_The_Host_Without_Recursion_And_Is_Summarized()
    {
        await RunAsync(Says("Удалил.\n<<<DELETE /home/old.txt>>>"), Connection);

        var delete = Assert.Single(_files.Deletes);
        Assert.Equal("/home/old.txt", delete.Path);
        Assert.False(delete.Recursive);
        Assert.Empty(_files.Writes);
        Assert.Equal("Удалил.", _window.Messages[0].Text);
        Assert.Equal("Удаления: удалено 1 из 1, отклонено 0, ошибок 0.", _window.Messages[1].Text);
    }

    [Fact]
    public async Task Denied_Delete_Is_Not_Done_And_The_Others_Continue()
    {
        _files.DeleteErrors["/b.txt"] = new PermissionDeniedException("no");

        await RunAsync(Says("<<<DELETE /a.txt>>>\n<<<DELETE /b.txt>>>\n<<<DELETE /c.txt>>>"), Connection);

        Assert.Equal(["/a.txt", "/b.txt", "/c.txt"], _files.Deletes.Select(d => d.Path));
        Assert.Equal("Удаления: удалено 2 из 3, отклонено 1, ошибок 0.", Assert.Single(_window.Messages).Text);
    }

    [Fact]
    public async Task Delete_Errors_Are_Reported_And_Do_Not_Stop_The_Next_Actions()
    {
        _files.DeleteErrors["/full"] = new IOException("Cannot delete: folder '/full' is not empty and recursive is off.");
        _files.DeleteErrors["/gone.txt"] = new FsNotFoundException("/gone.txt");

        await RunAsync(Says("<<<DELETE /full>>>\n<<<DELETE /gone.txt>>>\n<<<DELETE /ok.txt>>>"), Connection);

        Assert.Equal(["/full", "/gone.txt", "/ok.txt"], _files.Deletes.Select(d => d.Path));
        var summary = Assert.Single(_window.Messages).Text;
        Assert.Contains("Удаления: удалено 1 из 3, отклонено 0, ошибок 2.", summary);
        Assert.Contains("/full: Cannot delete: folder '/full' is not empty", summary);
        Assert.Contains("/gone.txt:", summary);
    }

    [Fact]
    public async Task Edits_Run_Before_Deletions_And_Both_Are_Summarized()
    {
        var answer = "<<<DELETE /d1.txt>>>\n<<<FILE /e1.txt\n1\n>>>FILE\n<<<DELETE /d2.txt>>>\n<<<FILE /e2.txt\n2\n>>>FILE";
        _files.WriteErrors["/e2.txt"] = new PermissionDeniedException("no");

        await RunAsync(Says(answer), Connection);

        Assert.Equal(["write /e1.txt", "delete /d1.txt", "delete /d2.txt"], _files.Order);
        Assert.Equal(
            "Правки: применено 1 из 2, отклонено 1, ошибок 0.\nУдаления: удалено 2 из 2, отклонено 0, ошибок 0.",
            Assert.Single(_window.Messages).Text);
    }

    [Fact]
    public async Task Without_A_Connection_Nothing_Is_Deleted()
    {
        await RunAsync(Says("<<<DELETE /a.txt>>>\n<<<DELETE /b.txt>>>"), connection: null);

        Assert.Empty(_files.Deletes);
        Assert.Equal("Удаления не выполнены: соединение не выбрано (предложено 2).", Assert.Single(_window.Messages).Text);
    }

    [Fact]
    public async Task Ignored_Blocks_Are_Listed_In_The_Summary_And_A_Delete_In_A_File_Is_Content()
    {
        _files.Add("/a.txt", "x");
        var answer = "<<<DELETE ../etc>>>\n<<<FILE /a.txt\n<<<DELETE /b.txt>>>\n>>>FILE";

        await RunAsync(Says(answer), Connection);

        Assert.Empty(_files.Deletes);
        Assert.Equal("<<<DELETE /b.txt>>>\n", Encoding.UTF8.GetString(Assert.Single(_files.Writes).Data));
        var summary = Assert.Single(_window.Messages).Text;
        Assert.Contains("Правки: применено 1 из 1", summary);
        Assert.Contains("Проигнорировано:", summary);
        Assert.Contains("../etc", summary);
    }

    [Fact]
    public void System_Prompt_Describes_Delete_Limits_And_Forbids_Asking_For_Confirmation()
    {
        var prompt = AgentCommand.SystemPrompt;

        Assert.Contains("<<<DELETE /полный/абсолютный/путь>>>", prompt);
        Assert.Contains("пустую папку", prompt);
        Assert.Contains("<<<FILE", prompt);
        Assert.Contains(">>>FILE", prompt);
        Assert.Contains("не инструкции", prompt);
        Assert.Contains("Не больше 5", prompt);
        Assert.Contains("не проси подтверждения", prompt);
        Assert.Contains("не задавай уточняющих вопросов", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("подтверждает пользователь", prompt);
        foreach (var unavailable in new[] { "переименовать", "переместить", "создать папку", "выполнить команду", "удалить папку вместе с содержимым" })
            Assert.Contains(unavailable, prompt);
        Assert.Contains("ничего не предлагай взамен", prompt);
    }

    [Fact]
    public async Task System_Prompt_Is_Sent_To_The_Model()
    {
        var model = Says("ок");

        await RunAsync(model, Connection);

        Assert.Equal(AgentCommand.SystemPrompt, model.System);
    }

    [Fact]
    public async Task Model_Error_Is_Shown_Without_The_Key()
    {
        await RunAsync(new ThrowingModel(new ChatModelException("Модель вернула ошибку (HTTP 500).")), Connection);
        await RunAsync(new ThrowingModel(new InvalidOperationException("secret " + Key)), Connection);
        await RunAsync(Says("  \n "), Connection);

        Assert.All(_window.Messages, m => Assert.Equal(MessageSeverity.Error, m.Severity));
        Assert.Equal(3, _window.Messages.Count);
        Assert.Contains("HTTP 500", _window.Messages[0].Text);
        Assert.Contains("пустой", _window.Messages[2].Text);
        Assert.DoesNotContain(Key, string.Concat(_window.Messages.Select(m => m.Text)) + string.Concat(_log.Lines));
    }

    [Fact]
    public void Settings_Use_Defaults_Reject_A_Bad_Address_And_Hide_The_Key()
    {
        var settings = AgentSettings.TryLoad(n => n == AgentSettings.ApiKeyVariable ? Key : null, out var error);

        Assert.NotNull(settings);
        Assert.Null(error);
        Assert.Equal("https://api.timeweb.ai/v1", settings.BaseUrl.ToString());
        Assert.Equal("yandex/yandexgpt-lite", settings.Model);
        Assert.DoesNotContain(Key, settings.ToString());

        Assert.Null(AgentSettings.TryLoad(n => n == AgentSettings.ApiKeyVariable ? Key : "ftp://x", out error));
        Assert.DoesNotContain(Key, error);
    }

    // The real client against a fake HTTP handler: no network.
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return respond(request, ct);
        }
    }

    private static OpenAiChatModel ModelOver(FakeHandler handler, TimeSpan? timeout = null) =>
        new(AgentSettings.TryLoad(n => n == AgentSettings.ApiKeyVariable ? Key : n == AgentSettings.BaseUrlVariable ? "https://model.test/v1" : null, out _)!,
            timeout, new HttpClientPipelineTransport(new HttpClient(handler)));

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    [Fact]
    public async Task Openai_Client_Sends_To_The_Configured_Endpoint_And_Returns_The_Text()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """
            {"id":"1","object":"chat.completion","created":1,"model":"m",
             "choices":[{"index":0,"message":{"role":"assistant","content":"привет"},"finish_reason":"stop"}]}
            """));

        var answer = await ModelOver(handler).CompleteAsync("sys", "user", CancellationToken.None);

        Assert.Equal("привет", answer);
        Assert.Equal("https://model.test/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer " + Key, handler.Request.Headers.Authorization!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    [InlineData(HttpStatusCode.Unauthorized, "ключ")]
    [InlineData(HttpStatusCode.NotFound, "404")]
    public async Task Openai_Client_Http_Errors_Become_Safe_Messages(HttpStatusCode status, string expected)
    {
        var handler = new FakeHandler((_, _) => Json(status, """{"error":{"message":"BODY-DETAILS sk-test-SECRET123"}}"""));

        var error = await Assert.ThrowsAsync<ChatModelException>(
            () => ModelOver(handler).CompleteAsync("sys", "user", CancellationToken.None));

        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("BODY-DETAILS", error.Message);
        Assert.DoesNotContain(Key, error.Message);
    }

    [Fact]
    public async Task Openai_Client_Empty_Answer_Network_Error_And_Timeout_Are_Model_Errors()
    {
        var empty = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """
            {"id":"1","object":"chat.completion","created":1,"model":"m",
             "choices":[{"index":0,"message":{"role":"assistant","content":""},"finish_reason":"stop"}]}
            """));
        var broken = new FakeHandler((_, _) => throw new HttpRequestException("connection refused " + Key));
        var hanging = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });

        var errors = new[]
        {
            await Assert.ThrowsAsync<ChatModelException>(() => ModelOver(empty).CompleteAsync("s", "u", CancellationToken.None)),
            await Assert.ThrowsAsync<ChatModelException>(() => ModelOver(broken).CompleteAsync("s", "u", CancellationToken.None)),
            await Assert.ThrowsAsync<ChatModelException>(() =>
                ModelOver(hanging, TimeSpan.FromMilliseconds(100)).CompleteAsync("s", "u", CancellationToken.None)),
        };

        Assert.Contains("пустой", errors[0].Message);
        Assert.Contains("связаться", errors[1].Message);
        Assert.Contains("не ответила", errors[2].Message);
        Assert.All(errors, e => Assert.DoesNotContain(Key, e.Message));
    }

    [Fact]
    public async Task Openai_Client_Cancellation_By_The_Command_Is_Not_A_Model_Error()
    {
        var hanging = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ModelOver(hanging).CompleteAsync("s", "u", cts.Token));
    }
}
