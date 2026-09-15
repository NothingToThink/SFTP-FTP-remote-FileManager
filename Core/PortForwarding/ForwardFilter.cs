namespace Core.PortForwarding;

/// <summary>
/// Filter over the forwarding rule list. Every field is optional; unset fields match everything,
/// so an empty filter is the identity.
/// </summary>
public class ForwardFilter
{
    /// <summary>Case-insensitive substring over rule name, target host and the OpenSSH-style description.</summary>
    public string? Text { get; set; }

    public ForwardType? Type { get; set; }

    public ForwardState? State { get; set; }

    /// <summary>Matches a rule whose bind port or target port falls in the range.</summary>
    public int? PortMin { get; set; }
    public int? PortMax { get; set; }

    public IEnumerable<ForwardStatus> Apply(IEnumerable<ForwardStatus> source)
    {
        var query = source;

        if (Type is { } type)
            query = query.Where(s => s.Rule.Type == type);

        if (State is { } state)
            query = query.Where(s => s.State == state);

        if (PortMin is not null || PortMax is not null)
        {
            var min = PortMin ?? int.MinValue;
            var max = PortMax ?? int.MaxValue;
            query = query.Where(s =>
                InRange(s.ActualBindPort ?? s.Rule.BindPort, min, max) ||
                InRange(s.Rule.TargetPort, min, max));
        }

        if (!string.IsNullOrWhiteSpace(Text))
        {
            var needle = Text.Trim();
            query = query.Where(s =>
                Contains(s.Rule.Name, needle) ||
                Contains(s.Rule.TargetHost, needle) ||
                Contains(s.Rule.Describe(), needle));
        }

        return query
            .OrderBy(s => s.Rule.Type)
            .ThenBy(s => s.ActualBindPort ?? s.Rule.BindPort);
    }

    private static bool InRange(int? value, int min, int max) => value is { } v && v >= min && v <= max;

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
