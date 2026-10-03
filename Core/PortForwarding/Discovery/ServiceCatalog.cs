namespace Core.PortForwarding.Discovery;

/// <summary>
/// Well-known port-to-service mapping, limited to the things people actually tunnel over SSH.
/// Used to label discovered ports and to resolve a typed service name back to a port.
/// </summary>
public static class ServiceCatalog
{
    private static readonly Dictionary<int, string> ByPort = new()
    {
        [22] = "SSH",
        [25] = "SMTP",
        [80] = "HTTP",
        [443] = "HTTPS",
        [1433] = "MS SQL Server",
        [1521] = "Oracle DB",
        [2375] = "Docker",
        [2376] = "Docker (TLS)",
        [3000] = "Node / Grafana",
        [3306] = "MySQL",
        [3389] = "RDP",
        [4369] = "Erlang EPMD",
        [5000] = "Flask / Kestrel",
        [5432] = "PostgreSQL",
        [5601] = "Kibana",
        [5672] = "RabbitMQ",
        [5900] = "VNC",
        [6379] = "Redis",
        [8080] = "HTTP alt",
        [8086] = "InfluxDB",
        [8443] = "HTTPS alt",
        [8888] = "Jupyter",
        [9000] = "SonarQube / MinIO",
        [9090] = "Prometheus",
        [9200] = "Elasticsearch",
        [11211] = "Memcached",
        [15672] = "RabbitMQ UI",
        [27017] = "MongoDB",
    };

    public static string? Describe(int port) => ByPort.GetValueOrDefault(port);

    /// <summary>
    /// Ports whose service name matches the typed text, so "postgres" can autocomplete to 5432.
    /// </summary>
    public static IEnumerable<PortSuggestion> Search(string? text, int limit = 10)
    {
        IEnumerable<KeyValuePair<int, string>> matches = ByPort;

        if (!string.IsNullOrWhiteSpace(text))
        {
            var needle = text.Trim();
            matches = matches.Where(pair =>
                pair.Value.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                pair.Key.ToString().StartsWith(needle, StringComparison.Ordinal));
        }

        return matches
            .OrderBy(pair => pair.Key)
            .Take(limit)
            .Select(pair => new PortSuggestion(
                Port: pair.Key,
                Address: null,
                Service: pair.Value,
                Process: null,
                Source: SuggestionSource.Catalog));
    }
}
