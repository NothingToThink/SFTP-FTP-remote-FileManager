using FileManagerClient.Models;

namespace FileManagerClient.Commands;

/// <summary>
/// Команды плагинов с сервера (GET /commands) как команды реестра: попадают в палитру и в контекстное
/// меню файла. Выполнение — POST /commands/{id}/execute (его делает <c>execute</c>); диалоги плагина
/// приходят отдельно через хаб /hubs/ui. Список обновляется через <see cref="RefreshAsync"/>.
/// </summary>
public sealed class PluginCommandSource(
    Func<CancellationToken, Task<IReadOnlyList<PluginCommand>>> load,
    Func<PluginCommand, CommandContext, Task> execute,
    Action changed) : ICommandSource
{
    public const string CategoryPrefix = "Плагин: ";

    private IReadOnlyList<PluginCommand> _commands = [];

    public IReadOnlyList<PluginCommand> Commands => _commands;

    public IEnumerable<AppCommand> GetCommands() => _commands.Select(c => new AppCommand
    {
        Id = c.Id,
        Title = c.Title,
        Category = CategoryPrefix + DisplayName(c),
        Source = DisplayName(c),
        IconKey = "Line.Code",
        // У плагинной команды нет метаданных «работает с файлами» — показываем её и в меню файла.
        Context = AppCommand.FileContext,
        Keywords = ["plugin", "плагин", c.PluginId],
        Handler = ctx => execute(c, ctx),
    }).ToList();

    /// <summary>Перечитывает список. При ошибке оставляет прежний. true — список изменился.</summary>
    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PluginCommand> loaded;
        try
        {
            loaded = await load(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }

        if (loaded.Select(Key).SequenceEqual(_commands.Select(Key)))
            return false;
        _commands = loaded;
        changed();
        return true;
    }

    /// <summary>Забыть команды (смена сервера).</summary>
    public void Clear()
    {
        if (_commands.Count == 0)
            return;
        _commands = [];
        changed();
    }

    private static string DisplayName(PluginCommand c) => string.IsNullOrWhiteSpace(c.PluginName) ? c.PluginId : c.PluginName;

    private static (string, string, string) Key(PluginCommand c) => (c.Id, c.Title, DisplayName(c));
}
