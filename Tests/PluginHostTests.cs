using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Backend.Plugins;
using Core.Utils;
using FileManager.Plugins;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

// The plugin folder comes from REMOTE_FILE_MANAGER_DATA_DIR, which is process-wide: these tests must not run
// in parallel with anything else.
[CollectionDefinition(Name, DisableParallelization = true)]
public class PluginHostCollection
{
    public const string Name = "PluginHost";
}

/// <summary>A temporary data folder that REMOTE_FILE_MANAGER_DATA_DIR points to while the object lives.</summary>
public sealed class PluginDataDir : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("plugin-host-").FullName;
    private readonly string? _previous = Environment.GetEnvironmentVariable(AppPaths.DataDirEnvironmentVariable);

    public PluginDataDir()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirEnvironmentVariable, _root);
        PluginsFolder = Path.Combine(AppPaths.GetAppFolder(), PluginLoader.PluginsFolderName);
    }

    public string PluginsFolder { get; }

    /// <summary>Copies a plugin published by the Tests build (testplugins/&lt;name&gt;) into the plugins folder.</summary>
    public string AddPlugin(string published, string folderName)
    {
        var target = Path.Combine(PluginsFolder, folderName);
        var source = Path.Combine(AppContext.BaseDirectory, "testplugins", published);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        return target;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirEnvironmentVariable, _previous);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp folder; a leftover is harmless.
        }
    }
}

public record LogEntry(string Category, LogLevel Level, string Message);

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyCollection<LogEntry> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
    }
}

/// <summary>A hub client that answers dialogs and records what it was asked.</summary>
public sealed class UiTestClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    private UiTestClient(HubConnection connection) => _connection = connection;

    public string ConnectionId => _connection.ConnectionId!;
    public Channel<WireInput> Inputs { get; } = Channel.CreateUnbounded<WireInput>();
    public Channel<WireMessage> Messages { get; } = Channel.CreateUnbounded<WireMessage>();

    public static async Task<UiTestClient> ConnectAsync(WebApplicationFactory<Program> factory,
        string? inputAnswer = "answer", bool handleMessages = true, string? messageAnswer = "OK")
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/ui"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var client = new UiTestClient(connection);
        connection.On<WireInput, string?>("ShowInputBox", input =>
        {
            client.Inputs.Writer.TryWrite(input);
            return Task.FromResult(inputAnswer);
        });
        if (handleMessages)
        {
            connection.On<WireMessage, string?>("ShowMessage", message =>
            {
                client.Messages.Writer.TryWrite(message);
                return Task.FromResult(messageAnswer);
            });
        }

        await connection.StartAsync();
        var sessions = factory.Services.GetRequiredService<Backend.Ui.IUiSessionRegistry>();
        using var cts = new CancellationTokenSource(PluginHostTests.Timeout);
        while (!sessions.IsConnected(client.ConnectionId))
            await Task.Delay(10, cts.Token);
        return client;
    }

    public Task StopAsync() => _connection.StopAsync();

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

[Collection(PluginHostCollection.Name)]
public class PluginHostTests : IAsyncLifetime
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly PluginDataDir _data = new();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly List<IAsyncDisposable> _disposables = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var disposable in _disposables.AsEnumerable().Reverse())
        {
            try
            {
                await disposable.DisposeAsync();
            }
            catch
            {
                // A test may already have stopped it.
            }
        }

        _data.Dispose();
    }

    private WebApplicationFactory<Program> StartBackend()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(_logs)));
        _disposables.Add(factory);
        return factory;
    }

    private async Task<UiTestClient> ConnectAsync(WebApplicationFactory<Program> factory, string? inputAnswer = "answer",
        bool handleMessages = true, string? messageAnswer = "OK")
    {
        var client = await UiTestClient.ConnectAsync(factory, inputAnswer, handleMessages, messageAnswer);
        _disposables.Add(client);
        return client;
    }

    private static async Task<HttpResponseMessage> ExecuteAsync(WebApplicationFactory<Program> factory, string commandId,
        string? session, string body = """{"connectionId":null,"currentPath":"/home","selectedPaths":["/a","/b"]}""")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/commands/{commandId}/execute")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (session is not null)
            request.Headers.Add("X-UI-Session", session);
        return await factory.CreateClient().SendAsync(request);
    }

    private static async Task<T> ReadAsync<T>(Channel<T> channel) => await channel.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

    private async Task WaitForLogAsync(Func<LogEntry, bool> predicate)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!_logs.Entries.Any(predicate))
            await Task.Delay(10, cts.Token);
    }

    private static async Task<string[]> CommandIdsAsync(WebApplicationFactory<Program> factory)
    {
        var commands = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/commands");
        return commands.EnumerateArray().Select(c => c.GetProperty("id").GetString()!).Order().ToArray();
    }

    private static async Task<string> ErrorOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString()!;
    }

    [Fact]
    public async Task Without_Plugins_Folder_There_Are_No_Commands()
    {
        var factory = StartBackend();

        Assert.Empty(await CommandIdsAsync(factory));
    }

    [Fact]
    public async Task Sample_Plugin_Is_Listed_And_Its_Dialogs_Reach_The_Client()
    {
        _data.AddPlugin("Sample.Echo", "echo");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, inputAnswer: "привет");

        var commands = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/commands");
        var command = Assert.Single(commands.EnumerateArray());
        Assert.Equal("sample.echo.ask", command.GetProperty("id").GetString());
        Assert.Equal("Эхо: спросить и показать", command.GetProperty("title").GetString());
        Assert.Equal("sample.echo", command.GetProperty("pluginId").GetString());

        var response = await ExecuteAsync(factory, "sample.echo.ask", client.ConnectionId);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, accepted.GetProperty("runId").GetGuid());
        Assert.Equal("Введите текст", (await ReadAsync(client.Inputs)).Prompt);
        var message = await ReadAsync(client.Messages);
        Assert.Equal("info", message.Severity);
        Assert.Equal("Вы ввели: привет\nВыбрано: /a, /b", message.Message);
    }

    [Fact]
    public async Task Plugin_Loads_Its_Dependency_From_Its_Own_Folder_And_Shares_The_Sdk()
    {
        var folder = _data.AddPlugin("DepPlugin", "dep");
        Assert.True(File.Exists(Path.Combine(folder, "Test.DepLib.dll")));
        Assert.True(File.Exists(Path.Combine(folder, "FileManager.Plugins.Sdk.dll")), "a copy of the SDK must be there");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory);

        var response = await ExecuteAsync(factory, "test.dep.run", client.ConnectionId);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        // The dependency lives in the plugin's load context, the SDK in the Backend's default one.
        Assert.Equal("Hello, plugin!|lib:test.dep|sdk:Default", (await ReadAsync(client.Messages)).Message);
    }

    [Fact]
    public async Task Handler_Exception_Is_Shown_To_The_Client_As_An_Error()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory);

        var response = await ExecuteAsync(factory, "test.dep.fail", client.ConnectionId);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = await ReadAsync(client.Messages);
        Assert.Equal("error", message.Severity);
        Assert.Equal("Dep Plugin: boom", message.Message);
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("test.dep.fail"));
    }

    [Fact]
    public async Task Message_Without_Buttons_Reaches_The_Client_With_An_Empty_Array_And_Gets_Its_Answer()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, messageAnswer: null);

        await ExecuteAsync(factory, "test.dep.nobuttons", client.ConnectionId);

        var message = await ReadAsync(client.Messages);
        Assert.NotNull(message.Buttons);
        Assert.Empty(message.Buttons);
        await WaitForLogAsync(e => e.Category == "Plugin.test.dep" && e.Message == "nobuttons-answer:null");
    }

    [Fact]
    public async Task Handler_Exception_Is_Only_Logged_When_The_Client_Cannot_Show_It()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory, handleMessages: false);

        await ExecuteAsync(factory, "test.dep.fail", client.ConnectionId);

        await WaitForLogAsync(e => e.Level == LogLevel.Warning && e.Message.Contains("Could not show the error"));
        Assert.Contains("test.dep.fail", await CommandIdsAsync(factory));
    }

    [Fact]
    public async Task Client_Disconnect_Cancels_The_Handler()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory);

        // 202 comes back while the handler is still running.
        var response = await ExecuteAsync(factory, "test.dep.wait", client.ConnectionId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("started", (await ReadAsync(client.Messages)).Message);
        await WaitForLogAsync(e => e.Message == "wait-started");
        Assert.DoesNotContain(_logs.Entries, e => e.Message == "wait-cancelled");

        await client.StopAsync();

        await WaitForLogAsync(e => e.Category == "Plugin.test.dep" && e.Message == "wait-cancelled");
    }

    [Fact]
    public async Task Backend_Stop_Cancels_The_Handler_And_Waits_For_It()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory);
        await ExecuteAsync(factory, "test.dep.wait", client.ConnectionId);
        await WaitForLogAsync(e => e.Message == "wait-started");

        await factory.DisposeAsync();

        Assert.Contains(_logs.Entries, e => e.Category == "Plugin.test.dep" && e.Message == "wait-cancelled");
    }

    [Fact]
    public async Task Broken_Plugins_Do_Not_Stop_The_Others_From_Loading()
    {
        Directory.CreateDirectory(Path.Combine(_data.PluginsFolder, "no-manifest"));

        var broken = Directory.CreateDirectory(Path.Combine(_data.PluginsFolder, "a-broken-json"));
        File.WriteAllText(Path.Combine(broken.FullName, "plugin.json"), "{ not json");

        var missingMain = Directory.CreateDirectory(Path.Combine(_data.PluginsFolder, "b-missing-main"));
        File.WriteAllText(Path.Combine(missingMain.FullName, "plugin.json"),
            """{"id":"x.missing","displayName":"X","version":"1","main":"Nope.dll"}""");

        // A dll without IPlugin: the dependency library used as main.
        var noPlugin = _data.AddPlugin("DepPlugin", "c-no-plugin-type");
        File.WriteAllText(Path.Combine(noPlugin, "plugin.json"),
            """{"id":"x.noplugin","displayName":"X","version":"1","main":"Test.DepLib.dll"}""");

        _data.AddPlugin("DepPlugin", "d-good");
        _data.AddPlugin("DepPlugin", "e-same-id-again");
        _data.AddPlugin("Sample.Echo", "f-echo");
        var factory = StartBackend();

        Assert.Equal(["sample.echo.ask", "test.dep.fail", "test.dep.nobuttons", "test.dep.run", "test.dep.wait"],
            await CommandIdsAsync(factory));
        Assert.True(_logs.Entries.Count(e => e.Level == LogLevel.Error && e.Category.Contains("PluginLoader")) >= 4);
    }

    [Fact]
    public async Task Commands_Outside_The_Plugin_Prefix_Or_Not_In_The_Manifest_Are_Skipped()
    {
        var folder = _data.AddPlugin("DepPlugin", "dep");
        File.WriteAllText(Path.Combine(folder, "plugin.json"), """
            {"id":"test.dep","displayName":"Dep Plugin","version":"1","main":"Test.DepPlugin.dll",
             "contributes":{"commands":[
                {"id":"test.dep.run","title":"Run"},
                {"id":"test.dep.run","title":"Run again"},
                {"id":"fm.file.rename","title":"Steal"},
                {"id":"test.dependency.run","title":"Wrong prefix"},
                {"id":"other.plugin.cmd","title":"Other"}]}}
            """);
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory);

        // Only the valid, declared command is listed; the plugin also registers test.dep.undeclared.
        Assert.Equal(["test.dep.run"], await CommandIdsAsync(factory));
        foreach (var id in new[] { "fm.file.rename", "other.plugin.cmd", "test.dep.undeclared", "test.dep.fail" })
            Assert.Equal(HttpStatusCode.NotFound, (await ExecuteAsync(factory, id, client.ConnectionId)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await ExecuteAsync(factory, "test.dep.run", client.ConnectionId)).StatusCode);
        Assert.Contains("Hello", (await ReadAsync(client.Messages)).Message);
    }

    [Fact]
    public async Task Execute_Unknown_Command_Returns_404_With_Error()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory);

        var response = await ExecuteAsync(factory, "test.dep.nothing", client.ConnectionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("test.dep.nothing", await ErrorOfAsync(response));
    }

    [Fact]
    public async Task Execute_Without_Session_Header_Returns_400()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();

        var response = await ExecuteAsync(factory, "test.dep.run", session: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("X-UI-Session", await ErrorOfAsync(response));
    }

    [Fact]
    public async Task Execute_For_A_Client_That_Is_Not_Connected_Returns_400()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();

        var response = await ExecuteAsync(factory, "test.dep.run", "nobody");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nobody", await ErrorOfAsync(response));
    }

    [Fact]
    public async Task Execute_With_Invalid_Body_Returns_400()
    {
        _data.AddPlugin("DepPlugin", "dep");
        var factory = StartBackend();
        await using var client = await ConnectAsync(factory);

        var response = await ExecuteAsync(factory, "test.dep.run", client.ConnectionId, "{ not json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(await ErrorOfAsync(response));
    }

    [Fact]
    public async Task Files_Is_A_Stub_Until_It_Is_Implemented()
    {
        var factory = StartBackend();
        var files = factory.Services.GetRequiredService<IFileSystem>();

        await Assert.ThrowsAsync<NotImplementedException>(() => files.ListAsync(Guid.NewGuid(), "/"));
    }
}

public class SdkReferenceTests
{
    [Fact]
    public void Sdk_References_Only_The_Base_Class_Library()
    {
        var references = typeof(IPlugin).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.All(references, name => Assert.True(
            name is "mscorlib" or "netstandard" || name.StartsWith("System.", StringComparison.Ordinal),
            $"SDK must not reference '{name}'"));
    }
}
