using FileManagerClient.Commands;
using FileManagerClient.Models;

namespace Tests;

public class PluginCommandSourceTests
{
    private static PluginCommand Cmd(string id, string title, string? name = "Sample Echo") =>
        new() { Id = id, Title = title, PluginId = "sample.echo", PluginName = name };

    private sealed class Fake
    {
        public List<PluginCommand> Commands { get; set; } = [Cmd("sample.echo.ask", "Эхо: спросить и показать")];
        public bool Fail { get; set; }
        public int Changed { get; private set; }
        public List<string> Executed { get; } = [];
        public PluginCommandSource Source { get; }

        public Fake() => Source = new PluginCommandSource(
            _ => Fail ? throw new HttpRequestException("down") : Task.FromResult<IReadOnlyList<PluginCommand>>(Commands.ToList()),
            (c, _) => { Executed.Add(c.Id); return Task.CompletedTask; },
            () => Changed++);
    }

    [Fact]
    public async Task Refresh_publishes_commands_with_category_and_file_context()
    {
        var f = new Fake();
        Assert.True(await f.Source.RefreshAsync());
        var cmd = Assert.Single(f.Source.GetCommands());
        Assert.Equal("Плагин: Sample Echo", cmd.Category);
        Assert.Equal(AppCommand.FileContext, cmd.Context);
        Assert.Equal(1, f.Changed);
    }

    [Fact]
    public async Task Category_falls_back_to_plugin_id()
    {
        var f = new Fake { Commands = [Cmd("x.y.z", "Z", name: null)] };
        await f.Source.RefreshAsync();
        Assert.Equal("Плагин: sample.echo", f.Source.GetCommands().Single().Category);
    }

    [Fact]
    public async Task Unchanged_list_does_not_notify_and_changed_list_does()
    {
        var f = new Fake();
        await f.Source.RefreshAsync();
        Assert.False(await f.Source.RefreshAsync());
        Assert.Equal(1, f.Changed);

        f.Commands = [];
        Assert.True(await f.Source.RefreshAsync());
        Assert.Empty(f.Source.GetCommands());
    }

    [Fact]
    public async Task Failed_refresh_keeps_previous_commands()
    {
        var f = new Fake();
        await f.Source.RefreshAsync();
        f.Fail = true;
        Assert.False(await f.Source.RefreshAsync());
        Assert.Single(f.Source.GetCommands());
    }

    [Fact]
    public async Task Plugin_command_is_found_in_registry_and_executes()
    {
        var f = new Fake();
        var registry = new CommandRegistry();
        registry.AddSource(f.Source);
        await f.Source.RefreshAsync();

        var hit = Assert.Single(registry.Search("эхо"));
        Assert.Single(registry.ForContext(AppCommand.FileContext));
        await registry.ExecuteAsync(hit.Command);
        Assert.Equal(["sample.echo.ask"], f.Executed);

        f.Source.Clear();
        Assert.Empty(registry.Search("эхо"));
    }
}
