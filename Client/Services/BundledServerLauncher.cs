using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FileManagerClient.Services;

/// <summary>Как запускать сервер: исполняемый файл, аргументы, рабочая папка.</summary>
public sealed record ServerCommand(string FileName, IReadOnlyList<string> Args, string WorkDir, string Description, bool IsDev);

/// <summary>Поиск сервера: опубликованный ./server/Backend рядом с клиентом либо dev-репозиторий.</summary>
public static class ServerLocator
{
    public static string BinaryName => OperatingSystem.IsWindows() ? "Backend.exe" : "Backend";

    public static ServerCommand? FindPublished(string? baseDir = null)
    {
        baseDir ??= AppContext.BaseDirectory;
        var dir = Path.Combine(baseDir, "server");
        var exe = Path.Combine(dir, BinaryName);
        return File.Exists(exe) ? new ServerCommand(exe, [], dir, exe, false) : null;
    }

    public static ServerCommand? FindDev(string? baseDir = null)
    {
        var dir = new DirectoryInfo(baseDir ?? AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var csproj = Path.Combine(dir.FullName, "Backend", "Backend.csproj");
            if (!File.Exists(csproj)) continue;
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } h && File.Exists(h) ? h : "dotnet";
            return new ServerCommand(dotnet,
                ["run", "--project", csproj, "--no-launch-profile", "--nologo"],
                Path.Combine(dir.FullName, "Backend"), $"dotnet run --project {csproj}", true);
        }
        return null;
    }

    /// <summary>Выбор лаунчера по настройке serverMode и наличию сервера.</summary>
    public static IServerLauncher CreateLauncher(ClientSettings settings, string? baseDir = null)
    {
        var external = new ExternalServerLauncher(() => settings.ServerUrl);
        if (settings.ServerMode == "external") return external;
        var published = FindPublished(baseDir);
        if (published is not null) return new BundledServerLauncher(settings, published);
        var dev = FindDev(baseDir);
        if (settings.ServerMode == "bundled")
            return new BundledServerLauncher(settings, dev);
        // auto в dev-репозитории: уже работающий сервер по настроенному адресу переиспользуем
        return dev is not null ? new BundledServerLauncher(settings, dev, reuseExisting: true) : external;
    }
}

/// <summary>
/// Сервер как дочерний процесс клиента (см. описание в <see cref="IServerLauncher"/>).
/// </summary>
public sealed class BundledServerLauncher : IServerLauncher
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DevReadyTimeout = TimeSpan.FromSeconds(90);

    private readonly ClientSettings _settings;
    private readonly ServerCommand? _command;
    private readonly bool _reuseExisting;
    private readonly object _logLock = new();
    private readonly Queue<string> _tail = new();
    private StreamWriter? _log;
    private Process? _process;
    private string _url;
    private bool _exitHooked;
    private readonly List<System.Runtime.InteropServices.PosixSignalRegistration> _signals = [];

    public BundledServerLauncher(ClientSettings settings, ServerCommand? command, bool reuseExisting = false)
    {
        _settings = settings;
        _command = command;
        _reuseExisting = reuseExisting;
        _url = settings.ServerUrl;
    }

    public bool IsManaged => true;
    public string ServerUrl => _url;
    public static string LogPath => Path.Combine(ClientSettings.DataDirectory, "logs", "server.log");
    private static string PidPath => Path.Combine(ClientSettings.DataDirectory, "logs", "server.pid");

    public async Task<bool> IsServerReachableAsync(CancellationToken ct = default)
        => await Probe(_url, ct);

    public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
        => (await StartAsync(ct)).Ok;

    private static async Task<bool> Probe(string url, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var resp = await http.GetAsync($"{url.TrimEnd('/')}/connections", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
    }

    public async Task<ServerStartResult> StartAsync(CancellationToken ct = default)
    {
        if (_process is { HasExited: false } && await Probe(_url, ct))
            return ServerStartResult.Success;
        if (_reuseExisting && _process is null && await Probe(_settings.ServerUrl, ct))
        {
            _url = _settings.ServerUrl; // dev: сервер уже запущен вручную - не трогаем
            return ServerStartResult.Success;
        }
        if (_command is null)
            return new ServerStartResult(false, "Сервер не найден",
                $"Не найден файл сервера: ожидался {Path.Combine(AppContext.BaseDirectory, "server", ServerLocator.BinaryName)}. " +
                "Переустановите приложение или запустите сервер вручную и укажите serverMode=external в настройках.");

        KillProcess();
        KillStaleFromPidFile();
        int port;
        try { port = FindFreePort(); }
        catch (Exception ex)
        {
            return new ServerStartResult(false, "Порт занят", "Не удалось найти свободный локальный порт для сервера.", ex.Message);
        }
        _url = $"http://127.0.0.1:{port}";

        OpenLog();
        var psi = new ProcessStartInfo(_command.FileName)
        {
            WorkingDirectory = _command.WorkDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in _command.Args) psi.ArgumentList.Add(a);
        psi.Environment["ASPNETCORE_URLS"] = _url;
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        if (_command.IsDev) psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        WriteLog($"[client] {DateTime.Now:O} start: {_command.Description} at {_url}");
        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            WriteLog($"[client] start failed: {ex}");
            return new ServerStartResult(false, "Сервер не запустился",
                "Не удалось запустить файл сервера. Проверьте, что он не заблокирован антивирусом и доступен для запуска.",
                $"{ex.Message}\n{_command.FileName}");
        }
        if (process is null)
            return new ServerStartResult(false, "Сервер не запустился", "Операционная система не запустила процесс сервера.", _command.FileName);

        _process = process;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) WriteLog(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) WriteLog(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try { File.WriteAllText(PidPath, process.Id.ToString()); } catch { }
        HookExit();

        var timeout = _command.IsDev ? DevReadyTimeout : ReadyTimeout;
        var deadline = DateTime.UtcNow + timeout;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    await Task.Delay(200, CancellationToken.None); // дочитать stdout/stderr
                    var tail = TailText();
                    var busy = tail.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
                               || tail.Contains("Only one usage of each socket", StringComparison.OrdinalIgnoreCase);
                    return busy
                        ? new ServerStartResult(false, "Порт занят",
                            $"Порт {port} уже используется другой программой. Нажмите «Повторить» - будет выбран другой порт.", tail)
                        : new ServerStartResult(false, "Сервер не запустился",
                            $"Сервер неожиданно завершился с кодом {process.ExitCode}.", tail);
                }
                if (await Probe(_url, ct))
                {
                    _settings.UseRuntimeServerUrl(_url);
                    WriteLog("[client] server is ready");
                    return ServerStartResult.Success;
                }
                await Task.Delay(200, ct);
            }
        }
        catch (OperationCanceledException)
        {
            KillProcess();
            throw;
        }
        var details = TailText();
        KillProcess();
        return new ServerStartResult(false, "Сервер не отвечает",
            $"Сервер запущен, но не ответил за {(int)timeout.TotalSeconds} с.", details);
    }

    public Task ShutdownAsync()
    {
        KillProcess();
        return Task.CompletedTask;
    }

    private void HookExit()
    {
        if (_exitHooked) return;
        _exitHooked = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillProcess();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => KillProcess();
        // kill/Ctrl+C/закрытие терминала: без обработчика сервер остался бы сиротой
        foreach (var sig in new[] { System.Runtime.InteropServices.PosixSignal.SIGTERM, System.Runtime.InteropServices.PosixSignal.SIGINT, System.Runtime.InteropServices.PosixSignal.SIGHUP })
        {
            try
            {
                _signals.Add(System.Runtime.InteropServices.PosixSignalRegistration.Create(sig, ctx =>
                {
                    KillProcess();
                    ctx.Cancel = false; // дальше - стандартное завершение
                }));
            }
            catch { /* сигнал не поддерживается платформой */ }
        }
    }

    private void KillProcess()
    {
        var p = Interlocked.Exchange(ref _process, null);
        if (p is null) return;
        try { if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } }
        catch { /* уже завершился */ }
        try { File.Delete(PidPath); } catch { }
    }

    /// <summary>Если клиент прошлого запуска был убит жёстко, его сервер мог остаться сиротой.</summary>
    private static void KillStaleFromPidFile()
    {
        try
        {
            if (!File.Exists(PidPath) || !int.TryParse(File.ReadAllText(PidPath), out var pid)) return;
            using var old = Process.GetProcessById(pid);
            var name = old.ProcessName;
            if (name.Equals("Backend", StringComparison.OrdinalIgnoreCase) || name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                old.Kill(entireProcessTree: true);
        }
        catch { }
        try { File.Delete(PidPath); } catch { }
    }

    private static int FindFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }

    private void OpenLog()
    {
        lock (_logLock)
        {
            _tail.Clear();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                _log?.Dispose();
                _log = new StreamWriter(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            }
            catch { _log = null; }
        }
    }

    private void WriteLog(string line)
    {
        lock (_logLock)
        {
            _tail.Enqueue(line);
            while (_tail.Count > 20) _tail.Dequeue();
            try { _log?.WriteLine(line); } catch { }
        }
    }

    private string TailText()
    {
        lock (_logLock) return string.Join('\n', _tail) + $"\n\nЛог: {LogPath}";
    }
}
