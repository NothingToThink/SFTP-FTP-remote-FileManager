using FileManagerClient.Controls;

namespace FileManagerClient.Commands;

/// <summary>Контекст запуска команды. Для контекста «file» в <see cref="Target"/> лежит выбранный FileItem.</summary>
public sealed class CommandContext
{
    public object? Target { get; init; }

    /// <summary>Обработчик может попросить палитру остаться открытой (например, «Подключиться к серверу…»).</summary>
    public bool KeepPaletteOpen { get; set; }

    /// <summary>Если задано вместе с <see cref="KeepPaletteOpen"/>, палитра подставит этот текст в поиск.</summary>
    public string? NewQuery { get; set; }
}

/// <summary>
/// Вклад в реестр команд: и встроенная команда, и (позже) плагинная выглядят одинаково.
/// Плагин фазы 1 будет превращаться в AppCommand с Handler, который делает POST /commands/{id}/execute.
/// </summary>
public sealed class AppCommand
{
    public const string FileContext = "file";

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public required Func<CommandContext, Task> Handler { get; init; }

    /// <summary>Дополнительные слова для поиска (синонимы, английские названия).</summary>
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();

    /// <summary>Подсказка горячей клавиши, например «F2».</summary>
    public string? Shortcut { get; init; }

    /// <summary>Ключ нейтральной иконки (Assets/NeutralIcons.axaml), по умолчанию Line.Command.</summary>
    public string IconKey { get; init; } = "Line.Command";

    /// <summary>Иконка-свинка вместо нейтральной (для «особых» команд).</summary>
    public PigIconKind PigIcon { get; init; } = PigIconKind.None;

    /// <summary>null — команда только в палитре; «file» — ещё и в контекстном меню файла.</summary>
    public string? Context { get; init; }

    /// <summary>Возвращает причину недоступности либо null, если команду можно выполнить. Вычисляется при каждом показе.</summary>
    public Func<CommandContext?, string?>? DisabledReason { get; init; }

    /// <summary>false — не показывать при пустом запросе (подрезультаты вроде «Подключиться: prod-01»).</summary>
    public bool ShowWhenQueryEmpty { get; init; } = true;

    /// <summary>Откуда команда: «built-in» или имя плагина.</summary>
    public string Source { get; init; } = "built-in";

    public string? GetDisabledReason(CommandContext? ctx = null) => DisabledReason?.Invoke(ctx);
}

/// <summary>Динамический источник команд (профили серверов, плагины). Вызывается при каждом поиске.</summary>
public interface ICommandSource
{
    IEnumerable<AppCommand> GetCommands();
}
