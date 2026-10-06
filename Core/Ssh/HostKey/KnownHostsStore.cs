using System.Text.Json;

namespace Core.Ssh.HostKey;

/// <summary>
/// Known-hosts store backed by a JSON file in the application folder.
///
/// This is deliberately our own file rather than OpenSSH's <c>~/.ssh/known_hosts</c>: that format
/// supports hashed hostnames, wildcards, markers and several key encodings, and misparsing it
/// would silently weaken verification.
/// </summary>
public class KnownHostsStore : IHostKeyStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private readonly Lock _gate = new();
    private Dictionary<string, KnownHostEntry> _entries;

    public KnownHostsStore(string filePath)
    {
        _filePath = filePath;
        _entries = Load(filePath);
    }

    public HostKeyVerdict Verify(string host, int port, HostKeyInfo presented, out KnownHostEntry? stored)
    {
        ArgumentNullException.ThrowIfNull(presented);

        lock (_gate)
        {
            if (!_entries.TryGetValue(KnownHostEntry.KeyFor(host, port), out var entry))
            {
                stored = null;
                return HostKeyVerdict.Unknown;
            }

            stored = entry;

            // Algorithm is part of the identity: a server may legitimately offer several key types,
            // but the one we pinned must still match byte for byte.
            var matches = string.Equals(entry.Sha256Fingerprint, presented.Sha256Fingerprint, StringComparison.Ordinal)
                          && string.Equals(entry.Algorithm, presented.Algorithm, StringComparison.Ordinal);

            return matches ? HostKeyVerdict.Trusted : HostKeyVerdict.Mismatch;
        }
    }

    public void Trust(string host, int port, HostKeyInfo key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_gate)
        {
            var entry = new KnownHostEntry(host, port, key.Algorithm, key.Sha256Fingerprint, DateTimeOffset.UtcNow);
            _entries[entry.Key] = entry;
            Persist();
        }
    }

    public bool Forget(string host, int port)
    {
        lock (_gate)
        {
            if (!_entries.Remove(KnownHostEntry.KeyFor(host, port)))
                return false;

            Persist();
            return true;
        }
    }

    public IReadOnlyList<KnownHostEntry> List()
    {
        lock (_gate)
        {
            return _entries.Values.OrderBy(e => e.Host, StringComparer.OrdinalIgnoreCase)
                                  .ThenBy(e => e.Port)
                                  .ToList();
        }
    }

    private static Dictionary<string, KnownHostEntry> Load(string filePath)
    {
        if (!File.Exists(filePath))
            return new Dictionary<string, KnownHostEntry>(StringComparer.Ordinal);

        try
        {
            var json = File.ReadAllText(filePath);
            var entries = JsonSerializer.Deserialize<List<KnownHostEntry>>(json, SerializerOptions) ?? [];
            return entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A corrupt store must not silently turn into "trust everything": fail closed by
            // starting empty, which downgrades every host to first-use (or strict refusal).
            return new Dictionary<string, KnownHostEntry>(StringComparer.Ordinal);
        }
    }

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Write to a sibling temp file and move it into place so a crash mid-write cannot
        // leave a half-written store behind.
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_entries.Values.ToList(), SerializerOptions));
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
