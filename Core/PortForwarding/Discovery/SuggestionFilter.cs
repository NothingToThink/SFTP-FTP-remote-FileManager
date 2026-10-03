namespace Core.PortForwarding.Discovery;

/// <summary>
/// Filter over autocomplete candidates. All fields optional; an empty filter is the identity
/// apart from <see cref="Limit"/>.
/// </summary>
public class SuggestionFilter
{
    /// <summary>Matches a port prefix ("54" → 5432) or a service/process substring ("postg").</summary>
    public string? Text { get; set; }

    public int? PortMin { get; set; }
    public int? PortMax { get; set; }

    /// <summary>Keep only listeners bound to loopback — the ones that genuinely need a tunnel.</summary>
    public bool LoopbackOnly { get; set; }

    /// <summary>Ports that already have a forwarding rule, hidden from suggestions.</summary>
    public IReadOnlySet<int>? ExcludePorts { get; set; }

    public int Limit { get; set; } = 50;

    public IReadOnlyList<PortSuggestion> Apply(IEnumerable<PortSuggestion> source)
    {
        var query = source;

        if (LoopbackOnly)
            query = query.Where(s => s.IsLoopbackOnly);

        if (PortMin is { } min)
            query = query.Where(s => s.Port >= min);

        if (PortMax is { } max)
            query = query.Where(s => s.Port <= max);

        if (ExcludePorts is { Count: > 0 } excluded)
            query = query.Where(s => !excluded.Contains(s.Port));

        if (!string.IsNullOrWhiteSpace(Text))
        {
            var needle = Text.Trim();
            query = query.Where(s =>
                s.Port.ToString().StartsWith(needle, StringComparison.Ordinal) ||
                Contains(s.Service, needle) ||
                Contains(s.Process, needle));
        }

        return query
            // Scanned ports outrank catalog guesses, and loopback listeners outrank public ones.
            .OrderBy(s => s.Source)
            .ThenByDescending(s => s.IsLoopbackOnly)
            .ThenBy(s => s.Port)
            .Take(Math.Clamp(Limit, 1, 500))
            .ToList();
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
