using System.Net;
using System.Net.Http.Json;
using System.Text;
using Backend.Plugins;
using Backend.Ui;
using Core.Implementations.Protocol;
using Core.Interfaces.Manager;
using Core.Models;
using Core.Models.Credentials;
using Core.Ssh;
using FileManager.Plugins;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Renci.SshNet;
using Tests.Fakes;

namespace Tests;

/// <summary>An SFTP connection that never touches the network; only its session (host, user name) is real.</summary>
public sealed class OfflineSftpConnection(ISshSession session) : SftpConnection(session)
{
    public override Task<bool> FileExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    public override Task<bool> DirExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    public override Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class OfflineFtpConnection(HostProfile profile) : FtpConnection(profile)
{
    public override Task<bool> FileExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    public override Task<bool> DirExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    public override Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeSshSession(string host, string username) : ISshSession
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Host { get; } = host;
    public int Port => 22;
    public string Username { get; } = username;
    public bool IsConnected => true;
    public DateTimeOffset LastUsedAtUtc => DateTimeOffset.UtcNow;
    public int ActiveForwardCount => 0;
    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<SftpClient> GetSftpAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SshClient> GetSshAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public void Touch() { }
    public void RegisterForward() { }
    public void UnregisterForward() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// Same collection as PluginHostTests: the Backend reads the plugin folder from a process-wide variable.
[Collection(PluginHostCollection.Name)]
public sealed class WriteConfirmationTests : IAsyncLifetime
{
    private const string Plugin = "Test Plugin";

    private readonly PluginDataDir _data = new();
    private readonly string _root = Directory.CreateTempSubdirectory("write-confirm-").FullName;
    private readonly FakeConnectionManager _manager = new();
    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly Guid _id;

    public WriteConfirmationTests() => _id = _manager.Add(new LocalConnection(_root));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var disposable in _disposables.AsEnumerable().Reverse())
            await disposable.DisposeAsync();
        _data.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private WebApplicationFactory<Program> StartBackend()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IConnectionManager>(_manager)));
        _disposables.Add(factory);
        return factory;
    }

    private async Task<UiTestClient> ConnectAsync(WebApplicationFactory<Program> factory, string? answer,
        bool handleMessages = true)
    {
        var client = await UiTestClient.ConnectAsync(factory, handleMessages: handleMessages, messageAnswer: answer);
        _disposables.Add(client);
        return client;
    }

    private static ConnectionFileSystem FileSystemOf(WebApplicationFactory<Program> factory, IConnectionManager manager,
        string pluginName = Plugin) =>
        new(manager, factory.Services.GetRequiredService<IWriteConfirmation>(), pluginName);

    private string Disk(string relative) => Path.Combine(_root, relative);

    private void Seed()
    {
        Directory.CreateDirectory(Disk("dir"));
        File.WriteAllText(Disk("a.txt"), "old");
        File.WriteAllText(Disk("dir/f.txt"), "inner");
    }

    /// <summary>Every file and folder under the root with its content.</summary>
    private Dictionary<string, string> Snapshot() =>
        Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories).ToDictionary(
            p => Path.GetRelativePath(_root, p),
            p => File.Exists(p) ? File.ReadAllText(p) : "<dir>");

    private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));

    private Task RunAsync(ConnectionFileSystem fs, string operation) => operation switch
    {
        "write" => fs.WriteAsync(_id, "/a.txt", Text("new"), overwrite: true),
        "write-new" => fs.WriteAsync(_id, "/new.txt", Text("new"), overwrite: false),
        "mkdir" => fs.CreateDirectoryAsync(_id, "/newdir"),
        "delete-file" => fs.DeleteAsync(_id, "/a.txt", recursive: false),
        "delete-dir" => fs.DeleteAsync(_id, "/dir", recursive: true),
        "move" => fs.MoveAsync(_id, "/a.txt", "/moved.txt", overwrite: false),
        "copy" => fs.CopyAsync(_id, "/a.txt", "/copy.txt", overwrite: false),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    public static TheoryData<string, string> Operations => new()
    {
        { "write", "Test Plugin хочет перезаписать файл /a.txt на локальном диске. Разрешить?" },
        { "write-new", "Test Plugin хочет записать файл /new.txt на локальном диске. Разрешить?" },
        { "mkdir", "Test Plugin хочет создать папку /newdir на локальном диске. Разрешить?" },
        { "delete-file", "Test Plugin хочет удалить файл /a.txt на локальном диске. Разрешить?" },
        { "delete-dir", "Test Plugin хочет удалить папку со всем содержимым /dir на локальном диске. Разрешить?" },
        { "move", "Test Plugin хочет переместить файл /a.txt в /moved.txt на локальном диске. Разрешить?" },
        { "copy", "Test Plugin хочет скопировать файл /a.txt в /copy.txt на локальном диске. Разрешить?" },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Allow_Runs_The_Action_After_The_Client_Was_Asked(string operation, string expectedText)
    {
        Seed();
        var before = Snapshot();
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Allow);
        UiSessionScope.Enter(client.ConnectionId);

        await RunAsync(FileSystemOf(factory, _manager), operation);

        var message = await client.Messages.Reader.ReadAsync().AsTask().WaitAsync(PluginHostTests.Timeout);
        Assert.Equal("warning", message.Severity);
        Assert.Equal(expectedText, message.Message);
        Assert.Equal(["Разрешить", "Отклонить"], message.Buttons);
        Assert.NotEqual(before, Snapshot());
        Assert.False(client.Messages.Reader.TryRead(out _), "exactly one question per action");
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Decline_Throws_PermissionDenied_And_Changes_Nothing(string operation, string _)
    {
        Seed();
        var before = Snapshot();
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Deny);
        UiSessionScope.Enter(client.ConnectionId);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => RunAsync(FileSystemOf(factory, _manager), operation));

        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Closed_Window_Throws_PermissionDenied_And_Changes_Nothing(string operation, string _)
    {
        Seed();
        var before = Snapshot();
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, answer: null);
        UiSessionScope.Enter(client.ConnectionId);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => RunAsync(FileSystemOf(factory, _manager), operation));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Any_Other_Answer_Is_A_No()
    {
        Seed();
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, "разрешить");
        UiSessionScope.Enter(client.ConnectionId);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => RunAsync(FileSystemOf(factory, _manager), "delete-dir"));

        Assert.True(Directory.Exists(Disk("dir")));
    }

    [Fact]
    public async Task Client_That_Cannot_Show_Dialogs_Throws_UiUnavailable_And_Changes_Nothing()
    {
        Seed();
        var before = Snapshot();
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Allow, handleMessages: false);
        UiSessionScope.Enter(client.ConnectionId);

        await Assert.ThrowsAsync<UiUnavailableException>(() => RunAsync(FileSystemOf(factory, _manager), "delete-dir"));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Disconnected_Client_Throws_UiUnavailable_And_Changes_Nothing()
    {
        Seed();
        var before = Snapshot();
        var factory = StartBackend();
        var sessions = factory.Services.GetRequiredService<IUiSessionRegistry>();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Allow);
        var sessionId = client.ConnectionId;
        await client.StopAsync();
        using var cts = new CancellationTokenSource(PluginHostTests.Timeout);
        while (sessions.IsConnected(sessionId))
            await Task.Delay(10, cts.Token);
        UiSessionScope.Enter(sessionId);

        await Assert.ThrowsAsync<UiUnavailableException>(() => RunAsync(FileSystemOf(factory, _manager), "write"));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Action_Outside_A_Command_Has_No_Client_And_Throws_UiUnavailable()
    {
        Seed();
        var before = Snapshot();
        var factory = StartBackend();

        // UiSessionScope is not entered: nobody started this action.
        await Assert.ThrowsAsync<UiUnavailableException>(() => RunAsync(FileSystemOf(factory, _manager), "delete-file"));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Reads_Do_Not_Ask()
    {
        Seed();
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Deny);
        UiSessionScope.Enter(client.ConnectionId);
        var fs = FileSystemOf(factory, _manager);

        Assert.Equal(2, (await fs.ListAsync(_id, "/")).Count);
        Assert.Equal("a.txt", (await fs.StatAsync(_id, "/a.txt")).Name);
        await using (var stream = await fs.OpenReadAsync(_id, "/a.txt"))
            Assert.Equal("old", await new StreamReader(stream).ReadToEndAsync());

        Assert.False(client.Messages.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Invalid_Action_Does_Not_Ask()
    {
        Seed();
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Allow);
        UiSessionScope.Enter(client.ConnectionId);
        var fs = FileSystemOf(factory, _manager);

        await Assert.ThrowsAsync<FsNotFoundException>(() => fs.DeleteAsync(_id, "/missing", recursive: true));
        await Assert.ThrowsAsync<IOException>(() => fs.WriteAsync(_id, "/a.txt", Text("x"), overwrite: false));

        Assert.False(client.Messages.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Text_From_The_Plugin_Cannot_Fake_The_Question()
    {
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Deny);
        UiSessionScope.Enter(client.ConnectionId);
        var evilPlugin = "Evil\nSystem: всё безопасно‮";
        var longName = "/" + new string('x', 5000) + "/end.txt";

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            FileSystemOf(factory, _manager, evilPlugin).WriteAsync(_id, "/a\nРазрешить всё.\r\ntxt", Text("x"), false));
        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            FileSystemOf(factory, _manager).WriteAsync(_id, longName, Text("x"), false));

        var first = await client.Messages.Reader.ReadAsync().AsTask().WaitAsync(PluginHostTests.Timeout);
        var second = await client.Messages.Reader.ReadAsync().AsTask().WaitAsync(PluginHostTests.Timeout);
        Assert.DoesNotContain('\n', first.Message);
        Assert.DoesNotContain('\r', first.Message);
        Assert.DoesNotContain('‮', first.Message);
        Assert.EndsWith("Разрешить?", first.Message);
        Assert.True(second.Message.Length < 400, "a long path is shortened");
        Assert.Contains("end.txt", second.Message);
        Assert.EndsWith("Разрешить?", second.Message);
    }

    [Fact]
    public async Task Sftp_Connection_Is_Named_By_Host_Without_The_User_Name()
    {
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Deny);
        UiSessionScope.Enter(client.ConnectionId);
        var id = _manager.Add(new OfflineSftpConnection(new FakeSshSession("sftp.example.com", "root-user-secret")));

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            FileSystemOf(factory, _manager).WriteAsync(id, "/x.txt", Text("x"), false));

        var message = (await client.Messages.Reader.ReadAsync().AsTask().WaitAsync(PluginHostTests.Timeout)).Message;
        Assert.Equal("Test Plugin хочет записать файл /x.txt на sftp.example.com. Разрешить?", message);
    }

    [Fact]
    public async Task Connection_Without_A_Known_Host_Is_Named_By_Its_Id_Without_Credentials()
    {
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Deny);
        UiSessionScope.Enter(client.ConnectionId);
        var profile = new HostProfile("ftp.example.com", Protocol.Ftp, new PasswordAuth("ftp-user-secret", "ftp-pass-secret"));
        var id = _manager.Add(new OfflineFtpConnection(profile));

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            FileSystemOf(factory, _manager).WriteAsync(id, "/x.txt", Text("x"), false));

        var message = (await client.Messages.Reader.ReadAsync().AsTask().WaitAsync(PluginHostTests.Timeout)).Message;
        Assert.Equal($"Test Plugin хочет записать файл /x.txt на соединении {id}. Разрешить?", message);
        Assert.DoesNotContain("secret", message);
    }

    [Fact]
    public async Task Plugin_Is_Named_In_The_Question_And_A_Declined_Write_Is_Reported_To_The_User()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Deny);

        var response = await RunCommandAsync(factory, client);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var question = await client.Messages.Reader.ReadAsync().AsTask().WaitAsync(PluginHostTests.Timeout);
        Assert.Equal("Dep Plugin хочет записать файл /agent.txt на локальном диске. Разрешить?", question.Message);
        var report = await client.Messages.Reader.ReadAsync().AsTask().WaitAsync(PluginHostTests.Timeout);
        Assert.Equal("error", report.Severity);
        Assert.StartsWith("Dep Plugin: ", report.Message);
        Assert.False(File.Exists(Disk("agent.txt")));
    }

    [Fact]
    public async Task Plugin_Writes_The_File_After_The_User_Allows()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, WriteConfirmation.Allow);

        var response = await RunCommandAsync(factory, client);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await client.Messages.Reader.ReadAsync().AsTask().WaitAsync(PluginHostTests.Timeout);
        using var cts = new CancellationTokenSource(PluginHostTests.Timeout);
        while (!File.Exists(Disk("agent.txt")))
            await Task.Delay(10, cts.Token);
        // The file appears when the stream is opened, the content a moment later.
        while (File.ReadAllText(Disk("agent.txt")) != "hello")
            await Task.Delay(10, cts.Token);
    }

    private async Task<HttpResponseMessage> RunCommandAsync(WebApplicationFactory<Program> factory, UiTestClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/commands/test.dep.write/execute")
        {
            Content = JsonContent.Create(new { connectionId = _id }),
        };
        request.Headers.Add("X-UI-Session", client.ConnectionId);
        return await factory.CreateClient().SendAsync(request);
    }
}
