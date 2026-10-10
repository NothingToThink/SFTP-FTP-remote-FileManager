using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Ui;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class UiChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly WebApplicationFactory<Program> _factory;

    public UiChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private IUiSessionRegistry Registry => _factory.Services.GetRequiredService<IUiSessionRegistry>();
    private IUiBridge Bridge => _factory.Services.GetRequiredService<IUiBridge>();

    /// <summary>Test client with the same wire settings as the real one: camelCase JSON, long polling.</summary>
    private HubConnection CreateClient()
    {
        var server = _factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/ui"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    /// <summary>
    /// StartAsync returns after the handshake, which the server sends before it runs OnConnectedAsync,
    /// so wait until the session shows up in the registry.
    /// </summary>
    private async Task StartAsync(HubConnection client)
    {
        await client.StartAsync();
        using var cts = new CancellationTokenSource(Timeout);
        while (!Registry.IsConnected(client.ConnectionId!))
            await Task.Delay(10, cts.Token);
    }

    [Fact]
    public async Task Connect_RegistersSession_Disconnect_RemovesIt()
    {
        await using var client = CreateClient();
        await StartAsync(client);
        var id = client.ConnectionId!;

        Assert.True(Registry.IsConnected(id));
        Assert.Contains(Registry.Sessions, s => s.ConnectionId == id);

        var disconnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Registry.Disconnected += disconnected.SetResult;
        await client.StopAsync();

        Assert.Equal(id, await disconnected.Task.WaitAsync(Timeout));
        Assert.False(Registry.IsConnected(id));
    }

    [Fact]
    public async Task ShowMessage_DeliversRequestAndReturnsAnswer()
    {
        await using var client = CreateClient();
        JsonElement received = default;
        client.On<JsonElement, string?>("ShowMessage", request =>
        {
            received = request.Clone();
            return Task.FromResult<string?>("Разрешить");
        });
        await StartAsync(client);

        var answer = await Bridge.ShowMessageAsync(client.ConnectionId!, UiSeverity.Warning,
            "Записать файлы?", ["Разрешить", "Отклонить"]).WaitAsync(Timeout);

        Assert.Equal("Разрешить", answer);
        Assert.Equal("warning", received.GetProperty("severity").GetString());
        Assert.Equal("Записать файлы?", received.GetProperty("message").GetString());
        Assert.Equal(["Разрешить", "Отклонить"],
            received.GetProperty("buttons").EnumerateArray().Select(b => b.GetString()));
    }

    [Fact]
    public async Task ShowInputBox_DeliversRequestAndReturnsAnswer()
    {
        await using var client = CreateClient();
        JsonElement received = default;
        client.On<JsonElement, string?>("ShowInputBox", request =>
        {
            received = request.Clone();
            return Task.FromResult<string?>("hello");
        });
        await StartAsync(client);

        var answer = await Bridge.ShowInputBoxAsync(client.ConnectionId!,
            new InputBoxRequest("Пароль", Placeholder: "***", Password: true)).WaitAsync(Timeout);

        Assert.Equal("hello", answer);
        Assert.Equal("Пароль", received.GetProperty("prompt").GetString());
        Assert.Equal("***", received.GetProperty("placeholder").GetString());
        Assert.True(received.GetProperty("password").GetBoolean());
    }

    [Fact]
    public async Task DismissedDialog_ReturnsNull()
    {
        await using var client = CreateClient();
        client.On<JsonElement, string?>("ShowInputBox", _ => Task.FromResult<string?>(null));
        await StartAsync(client);

        var answer = await Bridge.ShowInputBoxAsync(client.ConnectionId!, new InputBoxRequest("?")).WaitAsync(Timeout);

        Assert.Null(answer);
    }

    [Fact]
    public async Task NotConnectedClient_ThrowsUiUnavailable()
    {
        await Assert.ThrowsAsync<UiUnavailableException>(() =>
            Bridge.ShowMessageAsync("no-such-client", UiSeverity.Info, "hi", ["OK"]));
        await Assert.ThrowsAsync<UiUnavailableException>(() =>
            Bridge.ShowInputBoxAsync("no-such-client", new InputBoxRequest("?")));
    }

    [Fact]
    public async Task DisconnectWhileWaiting_CompletesTheCall()
    {
        await using var client = CreateClient();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Never answers: the dialog stays open until the client goes away.
        client.On<JsonElement, string?>("ShowInputBox", async _ =>
        {
            received.SetResult();
            await Task.Delay(System.Threading.Timeout.Infinite);
            return null;
        });
        await StartAsync(client);

        var call = Bridge.ShowInputBoxAsync(client.ConnectionId!, new InputBoxRequest("?"));
        await received.Task.WaitAsync(Timeout);
        await client.StopAsync();

        await Assert.ThrowsAsync<UiUnavailableException>(() => call.WaitAsync(Timeout));
    }

    [Fact]
    public async Task CallerCancellation_StillCancelsTheCall()
    {
        await using var client = CreateClient();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.On<JsonElement, string?>("ShowInputBox", async _ =>
        {
            received.SetResult();
            await Task.Delay(System.Threading.Timeout.Infinite);
            return null;
        });
        await StartAsync(client);
        using var cts = new CancellationTokenSource();

        var call = Bridge.ShowInputBoxAsync(client.ConnectionId!, new InputBoxRequest("?"), cts.Token);
        await received.Task.WaitAsync(Timeout);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Timeout));
    }

    [Fact]
    public async Task DemoEndpoint_RunsBothDialogsOnTheSameClient()
    {
        await using var client = CreateClient();
        var prompts = new List<string?>();
        var finalMessage = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.On<JsonElement, string?>("ShowInputBox", request =>
        {
            prompts.Add(request.GetProperty("prompt").GetString());
            return Task.FromResult<string?>("abc");
        });
        client.On<JsonElement, string?>("ShowMessage", request =>
        {
            finalMessage.SetResult(request.Clone());
            return Task.FromResult<string?>("OK");
        });
        await StartAsync(client);

        var http = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/dev/ui/demo");
        request.Headers.Add("X-UI-Session", client.ConnectionId);
        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = await finalMessage.Task.WaitAsync(Timeout);
        Assert.Equal(["Введите строку"], prompts);
        Assert.Equal("info", message.GetProperty("severity").GetString());
        Assert.Equal("Вы ввели: abc", message.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DemoEndpoint_WithoutHeader_Returns400()
    {
        var response = await _factory.CreateClient().PostAsync("/dev/ui/demo", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task DemoEndpoint_WithUnknownClient_Returns400()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/dev/ui/demo");
        request.Headers.Add("X-UI-Session", "no-such-client");
        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("error").GetString()));
    }
}
