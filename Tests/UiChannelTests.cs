using System.Net;
using System.Text.Json;
using Backend.Ui;
using FileManager.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

// Wire format of the client methods as the client sees it (camelCase JSON, severity as a string).
public record WireMessage(string Severity, string Message, string[] Buttons);

public record WireInput(string Prompt, string? Value, string? Placeholder, bool Password);

public class UiChannelTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private IUiSessionRegistry Registry => factory.Services.GetRequiredService<IUiSessionRegistry>();
    private IUiBridge Bridge => factory.Services.GetRequiredService<IUiBridge>();

    private static async Task<T> WithTimeout<T>(Task<T> task) => await task.WaitAsync(Timeout);

    // OnConnectedAsync on the server may run slightly after StartAsync() returns on the client,
    // so registration is awaited, never assumed.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!condition())
            await Task.Delay(10, cts.Token);
    }

    private async Task<HubConnection> ConnectAsync(Action<HubConnection>? configure = null)
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/ui"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        configure?.Invoke(connection);
        await connection.StartAsync();
        await WaitUntilAsync(() => Registry.IsConnected(connection.ConnectionId!));
        return connection;
    }

    [Fact]
    public async Task Connect_And_Disconnect_Update_Registry()
    {
        var disconnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Registry.ClientDisconnected += disconnected.SetResult;

        await using var connection = await ConnectAsync();
        var id = connection.ConnectionId!;
        var session = Assert.Single(Registry.GetSessions(), s => s.ConnectionId == id);
        Assert.True(DateTimeOffset.UtcNow - session.ConnectedAt < TimeSpan.FromMinutes(1));

        await connection.StopAsync();
        Assert.Equal(id, await WithTimeout(disconnected.Task));
        Assert.False(Registry.IsConnected(id));
    }

    [Fact]
    public async Task ShowMessage_Answer_Reaches_Caller()
    {
        WireMessage? received = null;
        await using var connection = await ConnectAsync(c => c.On<WireMessage, string?>("ShowMessage", m =>
        {
            received = m;
            return Task.FromResult<string?>("Разрешить");
        }));

        var answer = await WithTimeout(Bridge.ShowMessageAsync(connection.ConnectionId!, MessageSeverity.Warning,
            "Записать файл?", ["Разрешить", "Отклонить"], CancellationToken.None));

        Assert.Equal("Разрешить", answer);
        Assert.Equal("warning", received!.Severity);
        Assert.Equal("Записать файл?", received.Message);
        Assert.Equal(["Разрешить", "Отклонить"], received.Buttons);
    }

    [Fact]
    public async Task ShowMessage_Dismissed_Returns_Null()
    {
        await using var connection = await ConnectAsync(c =>
            c.On<WireMessage, string?>("ShowMessage", _ => Task.FromResult<string?>(null)));

        var answer = await WithTimeout(Bridge.ShowMessageAsync(connection.ConnectionId!, MessageSeverity.Error,
            "Ошибка", ["OK"], CancellationToken.None));

        Assert.Null(answer);
    }

    [Fact]
    public async Task ShowInputBox_Answer_Reaches_Caller()
    {
        WireInput? received = null;
        await using var connection = await ConnectAsync(c => c.On<WireInput, string?>("ShowInputBox", i =>
        {
            received = i;
            return Task.FromResult<string?>("secret");
        }));

        var answer = await WithTimeout(Bridge.ShowInputBoxAsync(connection.ConnectionId!,
            new InputBoxRequest("Пароль", Placeholder: "введите", Password: true), CancellationToken.None));

        Assert.Equal("secret", answer);
        Assert.Equal(new WireInput("Пароль", null, "введите", true), received);
    }

    [Fact]
    public async Task Unknown_Client_Throws_UiUnavailable()
    {
        await Assert.ThrowsAsync<UiUnavailableException>(() =>
            Bridge.ShowMessageAsync("nobody", MessageSeverity.Info, "x", ["OK"], CancellationToken.None));
        await Assert.ThrowsAsync<UiUnavailableException>(() =>
            Bridge.ShowInputBoxAsync("nobody", new InputBoxRequest("x"), CancellationToken.None));
    }

    [Fact]
    public async Task Disconnect_While_Waiting_Completes_The_Call()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<string?>();
        await using var connection = await ConnectAsync(c => c.On<WireInput, string?>("ShowInputBox", _ =>
        {
            handlerStarted.SetResult();
            return never.Task;
        }));
        var id = connection.ConnectionId!;

        var call = Bridge.ShowInputBoxAsync(id, new InputBoxRequest("?"), CancellationToken.None);
        await WithTimeout(handlerStarted.Task.ContinueWith(_ => true));
        Assert.False(call.IsCompleted);

        await connection.StopAsync();

        await Assert.ThrowsAsync<UiUnavailableException>(() => call.WaitAsync(Timeout));
        await Assert.ThrowsAsync<UiUnavailableException>(() =>
            Bridge.ShowInputBoxAsync(id, new InputBoxRequest("?"), CancellationToken.None));
    }

    [Fact]
    public async Task Caller_Cancellation_Is_Not_Reported_As_Unavailable()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = await ConnectAsync(c => c.On<WireInput, string?>("ShowInputBox", _ =>
        {
            handlerStarted.SetResult();
            return new TaskCompletionSource<string?>().Task;
        }));
        using var cts = new CancellationTokenSource();

        var call = Bridge.ShowInputBoxAsync(connection.ConnectionId!, new InputBoxRequest("?"), cts.Token);
        await WithTimeout(handlerStarted.Task.ContinueWith(_ => true));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Client_Without_Handler_Gives_UiUnavailable_And_Backend_Survives()
    {
        // No ShowMessage / ShowInputBox handlers registered on the client.
        await using var connection = await ConnectAsync();
        var id = connection.ConnectionId!;

        await Assert.ThrowsAsync<UiUnavailableException>(() =>
            Bridge.ShowMessageAsync(id, MessageSeverity.Info, "x", ["OK"], CancellationToken.None).WaitAsync(Timeout));
        await Assert.ThrowsAsync<UiUnavailableException>(() =>
            Bridge.ShowInputBoxAsync(id, new InputBoxRequest("x"), CancellationToken.None).WaitAsync(Timeout));

        // The connection and the server are still usable.
        Assert.True(Registry.IsConnected(id));
        connection.On<WireInput, string?>("ShowInputBox", _ => Task.FromResult<string?>("ok"));
        Assert.Equal("ok", await WithTimeout(Bridge.ShowInputBoxAsync(id, new InputBoxRequest("x"), CancellationToken.None)));
    }

    [Fact]
    public async Task Demo_Endpoint_Runs_Both_Dialogs()
    {
        var info = new TaskCompletionSource<WireMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        WireInput? input = null;
        await using var connection = await ConnectAsync(c =>
        {
            c.On<WireInput, string?>("ShowInputBox", i =>
            {
                input = i;
                return Task.FromResult<string?>("привет");
            });
            c.On<WireMessage, string?>("ShowMessage", m =>
            {
                info.SetResult(m);
                return Task.FromResult<string?>("OK");
            });
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/dev/ui/demo");
        request.Headers.Add("X-UI-Session", connection.ConnectionId);
        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = await WithTimeout(info.Task);
        Assert.Equal("Введите строку", input!.Prompt);
        Assert.Equal("info", message.Severity);
        Assert.Equal("Вы ввели: привет", message.Message);
    }

    [Fact]
    public async Task Demo_Endpoint_Without_Header_Returns_400()
    {
        var response = await factory.CreateClient().PostAsync("/dev/ui/demo", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Demo_Endpoint_With_Unknown_Session_Returns_400()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/dev/ui/demo");
        request.Headers.Add("X-UI-Session", "nobody");
        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Demo_Endpoint_Is_Not_Mapped_Outside_Development()
    {
        using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var response = await production.CreateClient().PostAsync("/dev/ui/demo", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
