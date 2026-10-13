using System.Text;
using FileManager.Plugins;
using VolcanoBoys.Chat;

namespace Tests;

public class AgentChatTests
{
    private sealed class FakeWindow : IWindow
    {
        public List<(MessageSeverity Severity, string Message)> Messages { get; } = [];
        public string? Answer { get; set; } = "OK";

        public Task<string?> ShowMessageAsync(MessageSeverity severity, string message,
            IReadOnlyList<string>? buttons = null, CancellationToken ct = default)
        {
            Messages.Add((severity, message));
            return Task.FromResult(Answer);
        }

        public Task<string?> ShowInputBoxAsync(InputBoxOptions options, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeChatSession(params string?[] incoming) : IChatSession
    {
        private int _next;
        private readonly CancellationTokenSource _closed = new();

        public CancellationToken ClosedToken => _closed.Token;
        public List<ChatMessage> Sent { get; } = [];

        public Task<string?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult(_next < incoming.Length ? incoming[_next++] : null);

        public Task SendMessageAsync(ChatMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _closed.Cancel();
            _closed.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeChatService(FakeChatSession session) : IChatService
    {
        public ChatSessionOptions? Options { get; private set; }

        public Task<IChatSession> OpenAsync(ChatSessionOptions options, CancellationToken ct = default)
        {
            Options = options;
            return Task.FromResult<IChatSession>(session);
        }
    }

    private sealed class FakeModel(params string[] answers) : IChatModel
    {
        public List<IReadOnlyList<ChatTurn>> Histories { get; } = [];

        public Task<string> CompleteAsync(string system, IReadOnlyList<ChatTurn> history, CancellationToken ct)
        {
            Histories.Add(history.ToArray());
            return Task.FromResult(answers[Histories.Count - 1]);
        }
    }

    private sealed class FakeFiles : IFileSystem
    {
        public Dictionary<string, byte[]> Content { get; } = [];
        public Dictionary<string, byte[]> Writes { get; } = [];

        public Task<FileEntry> StatAsync(Guid connectionId, string path, CancellationToken ct = default) =>
            Content.TryGetValue(path, out var bytes)
                ? Task.FromResult(new FileEntry(path, path, false, bytes.Length, DateTimeOffset.UnixEpoch, null))
                : throw new FsNotFoundException(path);

        public Task<Stream> OpenReadAsync(Guid connectionId, string path, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream(Content[path]));

        public Task<IReadOnlyList<FileEntry>> ListAsync(Guid connectionId, string path, CancellationToken ct = default) => throw new NotSupportedException();
        public async Task WriteAsync(Guid connectionId, string path, Stream content, bool overwrite, CancellationToken ct = default)
        {
            using var output = new MemoryStream();
            await content.CopyToAsync(output, ct);
            Writes[path] = output.ToArray();
        }
        public Task CreateDirectoryAsync(Guid connectionId, string path, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid connectionId, string path, bool recursive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MoveAsync(Guid connectionId, string from, string to, bool overwrite, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CopyAsync(Guid connectionId, string from, string to, bool overwrite, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NullLog : IPluginLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private static ChatCommand CreateCommand(FakeWindow window, FakeChatService chat, FakeFiles files, FakeModel model,
        Dictionary<string, string?>? variables = null) =>
        new(window, chat, files, new NullLog(),
            name => variables is null
                ? name == ChatSettings.ApiKeyVariable ? "test-key" : null
                : variables.GetValueOrDefault(name),
            _ => model);

    [Fact]
    public async Task Chat_Passes_Conversation_History_And_Initial_File_Context()
    {
        var window = new FakeWindow();
        var chatSession = new FakeChatSession("Привет", "А что в файле?");
        var chat = new FakeChatService(chatSession);
        var files = new FakeFiles();
        files.Content["/work/readme.txt"] = Encoding.UTF8.GetBytes("Описание проекта");
        var model = new FakeModel("Здравствуйте!", "В файле описание проекта.");
        var command = CreateCommand(window, chat, files, model);

        await command.RunAsync(
            new CommandContext(Guid.NewGuid(), "/work", ["/work/readme.txt"], "test"),
            CancellationToken.None);

        Assert.Equal(2, model.Histories.Count);
        Assert.Collection(model.Histories[0],
            turn =>
            {
                Assert.Equal(ChatRole.User, turn.Role);
                Assert.Contains("Описание проекта", turn.Content);
                Assert.Contains("Привет", turn.Content);
            });
        Assert.Collection(model.Histories[1],
            turn => Assert.Equal(ChatRole.User, turn.Role),
            turn => Assert.Equal(new ChatTurn(ChatRole.Assistant, "Здравствуйте!"), turn),
            turn => Assert.Equal(new ChatTurn(ChatRole.User, "А что в файле?"), turn));
        Assert.Equal(
            [new ChatMessage(ChatMessageRole.Assistant, "Здравствуйте!"),
                new ChatMessage(ChatMessageRole.Assistant, "В файле описание проекта.")],
            chatSession.Sent);
        Assert.Contains("внешнему сервису ИИ", chat.Options!.WelcomeMessage);
    }

    [Fact]
    public async Task Chat_Shows_The_Proposed_File_And_Writes_Only_After_Confirmation()
    {
        var window = new FakeWindow { Answer = "Подтвердить запись" };
        var chatSession = new FakeChatSession("Создай файл /work/hello.txt", "/exit");
        var chat = new FakeChatService(chatSession);
        var files = new FakeFiles();
        var model = new FakeModel(
            """{"message":"Создам файл с этим содержимым.","actions":[{"type":"write","path":"/work/hello.txt","content":"Привет"}]}""");
        var command = CreateCommand(window, chat, files, model);

        await command.RunAsync(new CommandContext(Guid.NewGuid(), "/work", [], "test"), CancellationToken.None);

        Assert.Equal("Привет", Encoding.UTF8.GetString(files.Writes["/work/hello.txt"]));
        Assert.Contains(window.Messages, item => item.Message.Contains("Привет")
                                                && item.Message.Contains("/work/hello.txt"));
        Assert.Collection(chatSession.Sent,
            item => Assert.Contains("Создам файл", item.Content),
            item => Assert.Contains("Записан файл", item.Content));
    }

    [Fact]
    public async Task Chat_Does_Not_Read_File_Content_Unless_User_Approves_External_Disclosure()
    {
        var window = new FakeWindow { Answer = "Отмена" };
        var chatSession = new FakeChatSession("Прочитай /work/secret.txt", "/exit");
        var files = new FakeFiles();
        files.Content["/work/secret.txt"] = Encoding.UTF8.GetBytes("секрет");
        var model = new FakeModel(
            """{"message":"Прочитаю файл.","actions":[{"type":"read","path":"/work/secret.txt"}]}""");
        var command = CreateCommand(window, new FakeChatService(chatSession), files, model);

        await command.RunAsync(new CommandContext(Guid.NewGuid(), "/work", [], "test"), CancellationToken.None);

        Assert.Contains(window.Messages, item => item.Message.Contains("будет отправлено внешней модели"));
        Assert.DoesNotContain(model.Histories[0], turn => turn.Content.Contains("секрет"));
        Assert.Contains(chatSession.Sent, item => item.Content.Contains("содержимое не отправлялось"));
    }

    [Fact]
    public void Parser_Ignores_Unsafe_And_Unknown_File_Actions()
    {
        var response = ChatFileActionParser.Parse(
            """{"message":"План","actions":[{"type":"delete","path":"/../../secret"},{"type":"shell","path":"/tmp"},{"type":"mkdir","path":"/tmp/new"}]}""");

        Assert.Single(response.Actions);
        Assert.Equal("mkdir", response.Actions[0].Type);
        Assert.Contains("2 некорректных", response.Message);
    }

    [Fact]
    public async Task Missing_Api_Key_Shows_Error_Without_Opening_A_Chat()
    {
        var window = new FakeWindow();
        var chat = new FakeChatService(new FakeChatSession("Привет"));
        var model = new FakeModel("unused");
        var command = CreateCommand(window, chat, new FakeFiles(), model, new Dictionary<string, string?>());

        await command.RunAsync(new CommandContext(null, null, [], "test"), CancellationToken.None);

        Assert.Contains(window.Messages, item => item.Severity == MessageSeverity.Error
                                                && item.Message.Contains(ChatSettings.ApiKeyVariable));
        Assert.Null(chat.Options);
        Assert.Empty(model.Histories);
    }

    [Fact]
    public async Task Exit_Command_Ends_The_Conversation_Without_Another_Model_Call()
    {
        var window = new FakeWindow();
        var chat = new FakeChatService(new FakeChatSession("Привет", "/EXIT"));
        var model = new FakeModel("Ответ");
        var command = CreateCommand(window, chat, new FakeFiles(), model);

        await command.RunAsync(new CommandContext(null, null, [], "test"), CancellationToken.None);

        Assert.Single(model.Histories);
    }

    [Fact]
    public async Task Turn_Limit_Is_Shown_And_Chat_Remains_Open_Until_Closed()
    {
        var window = new FakeWindow();
        var incoming = Enumerable.Repeat<string?>("Продолжай", ChatCommand.MaxTurns)
            .Append("/exit").ToArray();
        var chatSession = new FakeChatSession(incoming);
        var chat = new FakeChatService(chatSession);
        var model = new FakeModel(Enumerable.Repeat("Ответ", ChatCommand.MaxTurns).ToArray());
        var command = CreateCommand(window, chat, new FakeFiles(), model);

        await command.RunAsync(new CommandContext(null, null, [], "test"), CancellationToken.None);

        Assert.Equal(ChatCommand.MaxTurns, model.Histories.Count);
        Assert.Contains(chatSession.Sent, message => message.Content.Contains("достигнут", StringComparison.OrdinalIgnoreCase));
    }
}
