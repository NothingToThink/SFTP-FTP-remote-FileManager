using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Core.PortForwarding.Discovery;
using Core.Ssh;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Core.PortForwarding;

/// <summary>
/// Tracks forwarding rules per SSH session and owns the lifetime of the underlying
/// <see cref="ForwardedPort"/> objects.
/// </summary>
public class PortForwardingManager(IRemotePortScanner scanner) : IPortForwardingManager
{
    private readonly ConcurrentDictionary<Guid, SessionForwards> _bySession = new();

    public async Task<ForwardStatus> StartAsync(ISshSession session, ForwardRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(rule);

        rule.Validate();

        var forwards = _bySession.GetOrAdd(session.Id, _ => new SessionForwards(session));
        EnsureBindAvailable(rule, forwards.Rules.Values);

        var ssh = await session.GetSshAsync(ct);
        var entry = new ForwardEntry(rule, session);

        try
        {
            var forwardedPort = Build(rule);
            entry.Attach(forwardedPort, ssh);

            ssh.AddForwardedPort(forwardedPort);
            forwardedPort.Start();

            // SSH.NET surfaces a refused remote bind through the Exception event rather than from
            // Start(), so give it a moment to land before we report success.
            await Task.Delay(TimeSpan.FromMilliseconds(150), ct);
            if (entry.Error is { } startupError)
                throw new InvalidOperationException($"Failed to start {rule.Describe()}: {startupError}");

            entry.MarkStarted(ResolveBoundPort(forwardedPort, rule));
            forwards.Rules[rule.Id] = entry;

            return entry.ToStatus();
        }
        catch
        {
            entry.Stop();
            throw;
        }
    }

    public Task<ForwardStatus> StopAsync(Guid sessionId, Guid ruleId, CancellationToken ct = default)
    {
        var entry = Find(sessionId, ruleId);
        entry.Stop();
        return Task.FromResult(entry.ToStatus());
    }

    public Task RemoveAsync(Guid sessionId, Guid ruleId, CancellationToken ct = default)
    {
        var entry = Find(sessionId, ruleId);
        entry.Stop();

        if (_bySession.TryGetValue(sessionId, out var forwards))
            forwards.Rules.TryRemove(ruleId, out _);

        return Task.CompletedTask;
    }

    public ForwardStatus Get(Guid sessionId, Guid ruleId) => Find(sessionId, ruleId).ToStatus();

    public IReadOnlyList<ForwardStatus> List(Guid sessionId, ForwardFilter? filter = null)
    {
        if (!_bySession.TryGetValue(sessionId, out var forwards))
            return [];

        var statuses = forwards.Rules.Values.Select(entry => entry.ToStatus());
        return (filter ?? new ForwardFilter()).Apply(statuses).ToList();
    }

    public async Task<IReadOnlyList<PortSuggestion>> SuggestTargetsAsync(
        ISshSession session,
        SuggestionFilter? filter = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        filter ??= new SuggestionFilter();

        IReadOnlyList<PortSuggestion> scanned;
        try
        {
            scanned = await scanner.ScanAsync(session, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The catalog alone still gives usable autocomplete, so a server that refuses exec
            // (restricted shell, no ss/netstat) degrades instead of breaking the feature.
            scanned = [];
        }

        // Don't suggest a target that this session already forwards.
        var excluded = List(session.Id)
            .Where(s => s.State == ForwardState.Active && s.Rule.TargetPort is not null)
            .Select(s => s.Rule.TargetPort!.Value)
            .ToHashSet();

        if (filter.ExcludePorts is { Count: > 0 } explicitlyExcluded)
            excluded.UnionWith(explicitlyExcluded);

        filter.ExcludePorts = excluded;

        var scannedPorts = scanned.Select(s => s.Port).ToHashSet();
        var catalog = ServiceCatalog.Search(filter.Text, limit: filter.Limit)
            .Where(s => !scannedPorts.Contains(s.Port));

        return filter.Apply(scanned.Concat(catalog));
    }

    public Task RemoveSessionAsync(Guid sessionId)
    {
        if (!_bySession.TryRemove(sessionId, out var forwards))
            return Task.CompletedTask;

        foreach (var entry in forwards.Rules.Values)
            entry.Stop();

        return Task.CompletedTask;
    }

    private ForwardEntry Find(Guid sessionId, Guid ruleId)
    {
        if (_bySession.TryGetValue(sessionId, out var forwards) && forwards.Rules.TryGetValue(ruleId, out var entry))
            return entry;

        throw new KeyNotFoundException($"Forwarding rule {ruleId} was not found for connection {sessionId}.");
    }

    /// <summary>
    /// Rejects a bind that would collide with another rule on the same session, or with a socket
    /// already open on this machine. Catching it here gives a clear error instead of an opaque
    /// failure from deep inside SSH.NET.
    /// </summary>
    private static void EnsureBindAvailable(ForwardRule rule, IEnumerable<ForwardEntry> existing)
    {
        var clash = existing.FirstOrDefault(e =>
            e.State == ForwardState.Active &&
            e.Rule.BindPort == rule.BindPort &&
            string.Equals(e.Rule.BindHost, rule.BindHost, StringComparison.OrdinalIgnoreCase));

        if (clash is not null)
            throw new InvalidOperationException(
                $"{rule.BindHost}:{rule.BindPort} is already used by forwarding rule '{clash.Rule.Name}'.");

        // A remote forward binds on the server, so there is nothing to probe locally. Port 0 means
        // "let the OS choose" and cannot collide.
        if (rule.Type == ForwardType.Remote || rule.BindPort == 0)
            return;

        if (!IPAddress.TryParse(rule.BindHost, out var bindAddress))
            return;

        try
        {
            var probe = new TcpListener(bindAddress, rule.BindPort);
            probe.Start();
            probe.Stop();
        }
        catch (SocketException)
        {
            throw new InvalidOperationException(
                $"Local port {rule.BindHost}:{rule.BindPort} is already in use by another process.");
        }
    }

    private static ForwardedPort Build(ForwardRule rule) => rule.Type switch
    {
        ForwardType.Local => new ForwardedPortLocal(
            rule.BindHost, (uint)rule.BindPort, rule.TargetHost!, (uint)rule.TargetPort!.Value),

        ForwardType.Remote => new ForwardedPortRemote(
            rule.BindHost, (uint)rule.BindPort, rule.TargetHost!, (uint)rule.TargetPort!.Value),

        ForwardType.Dynamic => new ForwardedPortDynamic(rule.BindHost, (uint)rule.BindPort),

        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Type, "Unsupported forward type."),
    };

    /// <summary>With bind port 0 the OS picks the port, so read back what was actually bound.</summary>
    private static int ResolveBoundPort(ForwardedPort port, ForwardRule rule) => port switch
    {
        ForwardedPortLocal local => (int)local.BoundPort,
        ForwardedPortRemote remote => (int)remote.BoundPort,
        ForwardedPortDynamic dynamicPort => (int)dynamicPort.BoundPort,
        _ => rule.BindPort,
    };

    private sealed class SessionForwards(ISshSession session)
    {
        public ISshSession Session { get; } = session;
        public ConcurrentDictionary<Guid, ForwardEntry> Rules { get; } = new();
    }

    /// <summary>Mutable runtime state for one rule. Entries are independent and lock separately.</summary>
    private sealed class ForwardEntry(ForwardRule rule, ISshSession session)
    {
        private readonly Lock _gate = new();
        private ForwardedPort? _port;
        private SshClient? _owner;
        private long _connectionsServed;
        private bool _counted;

        public ForwardRule Rule { get; } = rule;
        public ForwardState State { get; private set; } = ForwardState.Stopped;
        public string? Error { get; private set; }
        public int? BoundPort { get; private set; }
        public DateTimeOffset? StartedAtUtc { get; private set; }

        public void Attach(ForwardedPort port, SshClient owner)
        {
            lock (_gate)
            {
                _port = port;
                _owner = owner;
                port.Exception += OnException;
                port.RequestReceived += OnRequestReceived;
            }
        }

        public void MarkStarted(int boundPort)
        {
            lock (_gate)
            {
                BoundPort = boundPort;
                StartedAtUtc = DateTimeOffset.UtcNow;

                if (State != ForwardState.Failed)
                    State = ForwardState.Active;

                // The session's forward count keeps the idle janitor from collecting a transport
                // that is holding live tunnels, so count each entry exactly once.
                if (!_counted)
                {
                    session.RegisterForward();
                    _counted = true;
                }
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                try
                {
                    if (_port?.IsStarted == true)
                        _port.Stop();
                }
                catch (Exception e)
                {
                    Error = e.Message;
                }
                finally
                {
                    Detach();
                    State = ForwardState.Stopped;
                    StartedAtUtc = null;

                    if (_counted)
                    {
                        session.UnregisterForward();
                        _counted = false;
                    }
                }
            }
        }

        public ForwardStatus ToStatus()
        {
            lock (_gate)
            {
                return new ForwardStatus(
                    Rule,
                    State,
                    BoundPort,
                    Error,
                    StartedAtUtc,
                    Interlocked.Read(ref _connectionsServed));
            }
        }

        private void Detach()
        {
            if (_port is null)
                return;

            _port.Exception -= OnException;
            _port.RequestReceived -= OnRequestReceived;

            try
            {
                _owner?.RemoveForwardedPort(_port);
            }
            catch
            {
                // The client may already be disconnected or disposed.
            }

            _port.Dispose();
            _port = null;
            _owner = null;
        }

        private void OnException(object? sender, ExceptionEventArgs e)
        {
            lock (_gate)
            {
                Error = e.Exception.Message;
                State = ForwardState.Failed;
            }
        }

        private void OnRequestReceived(object? sender, PortForwardEventArgs e)
            => Interlocked.Increment(ref _connectionsServed);
    }
}
