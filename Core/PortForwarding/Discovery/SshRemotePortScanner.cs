using System.Text.RegularExpressions;
using Core.Ssh;

namespace Core.PortForwarding.Discovery;

/// <summary>
/// Enumerates listening TCP ports on the remote host over the SSH exec channel.
///
/// Prefers <c>ss</c> and falls back to <c>netstat</c>; both are read-only and do not require root
/// (without root the process column is simply empty, which is fine — the port is what matters).
/// </summary>
public partial class SshRemotePortScanner : IRemotePortScanner
{
    // -H drops the header, -l listening only, -t TCP, -n numeric, -p process (best effort).
    private const string ScanCommand =
        "command -v ss >/dev/null 2>&1 && ss -H -ltnp 2>/dev/null " +
        "|| netstat -ltnp 2>/dev/null " +
        "|| netstat -ltn 2>/dev/null";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    public async Task<IReadOnlyList<PortSuggestion>> ScanAsync(ISshSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var ssh = await session.GetSshAsync(ct);

        using var command = ssh.CreateCommand(ScanCommand);
        command.CommandTimeout = CommandTimeout;
        await command.ExecuteAsync(ct);

        // A non-zero exit means neither tool was available. That is not an error worth failing the
        // request over — the caller still has the service catalog to autocomplete from.
        if (string.IsNullOrWhiteSpace(command.Result))
            return [];

        return Parse(command.Result);
    }

    internal static IReadOnlyList<PortSuggestion> Parse(string output)
    {
        var results = new Dictionary<(string Address, int Port), PortSuggestion>();

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            // netstat prints a header; ss with -H does not, but the fallback netstat branch might.
            if (line.StartsWith("Proto", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Active", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryParseLine(line, out var address, out var port, out var process))
                continue;

            var key = (address, port);
            var suggestion = new PortSuggestion(
                Port: port,
                Address: address,
                Service: ServiceCatalog.Describe(port),
                Process: process,
                Source: SuggestionSource.RemoteScan);

            // A port can be listed once per address family; keep the entry that named a process.
            if (!results.TryGetValue(key, out var existing) || existing.Process is null)
                results[key] = suggestion;
        }

        return results.Values.OrderBy(s => s.Port).ToList();
    }

    private static bool TryParseLine(string line, out string address, out int port, out string? process)
    {
        address = string.Empty;
        port = 0;
        process = null;

        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4)
            return false;

        // ss:      LISTEN 0 4096 127.0.0.1:5432 0.0.0.0:*  users:(("postgres",pid=1,fd=7))
        // netstat: tcp    0 0    127.0.0.1:5432 0.0.0.0:*  LISTEN  1/postgres
        // In both, the local address is the first field that parses as address:port with a
        // numeric port; the peer field always ends in ":*" and so fails the parse.
        foreach (var field in fields)
        {
            if (TryParseEndpoint(field, out address, out port))
            {
                process = ExtractProcess(line);
                return true;
            }
        }

        return false;
    }

    private static bool TryParseEndpoint(string token, out string address, out int port)
    {
        address = string.Empty;
        port = 0;

        var separator = token.LastIndexOf(':');
        if (separator <= 0 || separator == token.Length - 1)
            return false;

        if (!int.TryParse(token.AsSpan(separator + 1), out port) || port is < 1 or > 65535)
            return false;

        address = token[..separator];

        // "*" and "0.0.0.0" both mean every interface; normalise so the UI is not confusing.
        if (address is "*" or "::" or "[::]")
            address = "0.0.0.0";

        // ss renders IPv6 as [::1]:5432, and also uses "%lo" style scope suffixes.
        address = address.Trim('[', ']');
        var scope = address.IndexOf('%');
        if (scope > 0)
            address = address[..scope];

        return address.Length > 0;
    }

    private static string? ExtractProcess(string line)
    {
        // ss:      users:(("postgres",pid=1234,fd=7))
        var ssMatch = SsProcessPattern().Match(line);
        if (ssMatch.Success)
            return ssMatch.Groups[1].Value;

        // netstat: 1234/postgres   (may be "-" when the process is not ours to see)
        var netstatMatch = NetstatProcessPattern().Match(line);
        return netstatMatch.Success ? netstatMatch.Groups[1].Value : null;
    }

    [GeneratedRegex(@"users:\(\(""([^""]+)""")]
    private static partial Regex SsProcessPattern();

    [GeneratedRegex(@"\s\d+/(\S+)\s*$")]
    private static partial Regex NetstatProcessPattern();
}
