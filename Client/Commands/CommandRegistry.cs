namespace FileManagerClient.Commands;

/// <summary>Результат поиска: команда, оценка и индексы подсвечиваемых символов заголовка.</summary>
public sealed record SearchResult(AppCommand Command, int Score, IReadOnlyList<int> TitleMatches);

/// <summary>Ранжирование поиска по палитре. Чистая логика, без UI — покрыта тестами.</summary>
public static class CommandSearch
{
    public static string Normalize(string s) =>
        s.Trim().ToLowerInvariant().Replace('ё', 'е');

    /// <summary>
    /// Каждое слово запроса обязано совпасть. Вес слова: начало слова в заголовке (100, 120 с начала заголовка)
    /// &gt; подстрока заголовка (70) &gt; категория/ключевые слова (40) &gt; символы по порядку (20, «нечётко»).
    /// </summary>
    public static SearchResult? Score(AppCommand cmd, string query)
    {
        var title = Normalize(cmd.Title);
        var category = Normalize(cmd.Category);
        var keywords = cmd.Keywords.Select(Normalize).ToList();
        var matches = new SortedSet<int>();
        var total = 0;

        foreach (var token in Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = WordStartIndex(title, token);
            if (idx >= 0)
            {
                total += idx == 0 ? 120 : 100;
                AddRange(matches, idx, token.Length);
                continue;
            }
            idx = title.IndexOf(token, StringComparison.Ordinal);
            if (idx >= 0)
            {
                total += 70;
                AddRange(matches, idx, token.Length);
                continue;
            }
            if (category.Contains(token, StringComparison.Ordinal) || keywords.Any(k => k.Contains(token, StringComparison.Ordinal)))
            {
                total += 40;
                continue;
            }
            if (token.Length >= 2 && Subsequence(title, token) is { } seq)
            {
                total += 20;
                foreach (var i in seq) matches.Add(i);
                continue;
            }
            return null;
        }
        return new SearchResult(cmd, total, matches.ToList());
    }

    private static int WordStartIndex(string title, string token)
    {
        for (var i = 0; i <= title.Length - token.Length; i++)
        {
            if ((i == 0 || !char.IsLetterOrDigit(title[i - 1])) && string.CompareOrdinal(title, i, token, 0, token.Length) == 0)
                return i;
        }
        return -1;
    }

    private static List<int>? Subsequence(string title, string token)
    {
        var result = new List<int>();
        var from = 0;
        foreach (var ch in token)
        {
            var i = title.IndexOf(ch, from);
            if (i < 0) return null;
            result.Add(i);
            from = i + 1;
        }
        return result;
    }

    private static void AddRange(SortedSet<int> set, int start, int len)
    {
        for (var i = start; i < start + len; i++) set.Add(i);
    }
}

/// <summary>
/// Реестр команд клиента. Команды регистрируются в рантайме (<see cref="Register"/>) либо приходят из
/// динамических источников (<see cref="AddSource"/>) — так плагинные команды появляются без перезапуска.
/// Любое изменение вызывает <see cref="Changed"/>.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, AppCommand> _commands = new();
    private readonly List<string> _order = new();
    private readonly List<ICommandSource> _sources = new();
    private readonly List<string> _recent = new();   // последние выполненные Id, самый свежий первым

    public event EventHandler? Changed;

    /// <summary>Добавить или заменить команду с тем же Id.</summary>
    public void Register(AppCommand command)
    {
        if (_commands.ContainsKey(command.Id))
            _order.Remove(command.Id);
        _commands[command.Id] = command;
        _order.Add(command.Id);
        RaiseChanged();
    }

    public bool Unregister(string id)
    {
        if (!_commands.Remove(id)) return false;
        _order.Remove(id);
        RaiseChanged();
        return true;
    }

    public void AddSource(ICommandSource source)
    {
        _sources.Add(source);
        RaiseChanged();
    }

    /// <summary>Источники сами не знают про реестр — после смены их содержимого вызовите это.</summary>
    public void NotifyChanged() => RaiseChanged();

    public IReadOnlyList<AppCommand> All()
    {
        var list = _order.Select(id => _commands[id]).ToList();
        foreach (var s in _sources)
            list.AddRange(s.GetCommands());
        return list;
    }

    /// <summary>Команды для контекстного меню (например, «file»).</summary>
    public IReadOnlyList<AppCommand> ForContext(string context) =>
        All().Where(c => c.Context == context).ToList();

    public void MarkUsed(AppCommand cmd)
    {
        _recent.Remove(cmd.Id);
        _recent.Insert(0, cmd.Id);
        if (_recent.Count > 8) _recent.RemoveAt(_recent.Count - 1);
    }

    /// <summary>Пустой запрос: недавние первыми, затем остальные в порядке регистрации. Иначе — по убыванию оценки.</summary>
    public IReadOnlyList<SearchResult> Search(string? query)
    {
        var all = All();
        if (string.IsNullOrWhiteSpace(query))
        {
            var visible = all.Where(c => c.ShowWhenQueryEmpty).ToList();
            var recent = _recent.Select(id => visible.FirstOrDefault(c => c.Id == id)).OfType<AppCommand>().ToList();
            return recent.Concat(visible.Except(recent)).Select(c => new SearchResult(c, 0, Array.Empty<int>())).ToList();
        }

        return all
            .Select(c => CommandSearch.Score(c, query))
            .OfType<SearchResult>()
            .OrderByDescending(r => r.Score + RecentBonus(r.Command))   // стабильно: при равенстве — порядок регистрации
            .ToList();
    }

    private int RecentBonus(AppCommand c)
    {
        var i = _recent.IndexOf(c.Id);
        return i < 0 ? 0 : 8 - i;
    }

    public async Task ExecuteAsync(AppCommand command, CommandContext? ctx = null)
    {
        ctx ??= new CommandContext();
        MarkUsed(command);
        await command.Handler(ctx);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
