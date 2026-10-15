using System.Diagnostics;
using Core.Ssh.HostKey;
using Core.Utils;

namespace Tests;

/// <summary>tools/demo-fake-hostkey.sh должен писать в known_hosts.json запись, которую KnownHostsStore читает как несовпадение.</summary>
public class DemoFakeHostKeyScriptTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tools", "demo-fake-hostkey.sh")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static int Run(string dataDir, params string[] args)
    {
        var psi = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Path.Combine(RepoRoot(), "tools", "demo-fake-hostkey.sh"));
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment[AppPaths.DataDirEnvironmentVariable] = dataDir;
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    [Fact]
    public void Fake_entry_gives_mismatch_and_restore_removes_it()
    {
        if (OperatingSystem.IsWindows()) return; // на Windows - tools/demo-fake-hostkey.ps1
        var dir = Path.Combine(Path.GetTempPath(), "swine-hostkey-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(dir, "RemoteFileManager", "known_hosts.json");
            var real = new HostKeyInfo("ssh-ed25519", "REALFINGERPRINTxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");

            Assert.Equal(0, Run(dir, "demo.example.com", "2222"));
            Assert.Equal(HostKeyVerdict.Mismatch, new KnownHostsStore(file).Verify("Demo.Example.com", 2222, real, out _));

            Assert.Equal(0, Run(dir, "demo.example.com", "2222")); // повторный запуск не плодит дубликаты
            Assert.Single(new KnownHostsStore(file).List());

            Assert.Equal(0, Run(dir, "--restore", "demo.example.com", "2222"));
            Assert.Equal(HostKeyVerdict.Unknown, new KnownHostsStore(file).Verify("demo.example.com", 2222, real, out _));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
