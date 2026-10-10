using System.Text;
using Backend.Plugins;
using Core.Implementations.Protocol;
using FileManager.Plugins;
using Renci.SshNet.Common;
using Tests.Fakes;

namespace Tests;

/// <summary>Confirmation that always says yes and remembers what it was asked.</summary>
public sealed class AllowingConfirmation : IWriteConfirmation
{
    public List<string> Asked { get; } = [];

    public Task ConfirmAsync(string pluginName, Guid connectionId, string action, string path, string? destination,
        CancellationToken ct)
    {
        Asked.Add($"{pluginName}|{action}|{path}|{destination}");
        return Task.CompletedTask;
    }
}

/// <summary>A LocalConnection that fails the way SFTP or the OS would.</summary>
public sealed class FaultyConnection(string root, Exception error) : LocalConnection(root)
{
    public override Task<bool> FileExistsAsync(string path, CancellationToken ct = default) => throw error;
}

public sealed class PluginFileSystemTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("plugin-fs-").FullName;
    private readonly FakeConnectionManager _manager = new();
    private readonly AllowingConfirmation _confirmation = new();
    private readonly Guid _id;
    private readonly ConnectionFileSystem _fs;

    public PluginFileSystemTests()
    {
        _id = _manager.Add(new LocalConnection(_root));
        _fs = new ConnectionFileSystem(_manager, _confirmation, "Test Plugin");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Disk(string relative) => Path.Combine(_root, relative);

    private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadAllAsync(Stream stream)
    {
        await using (stream)
            return await new StreamReader(stream).ReadToEndAsync();
    }

    [Fact]
    public async Task Write_Then_OpenRead_Returns_The_Content()
    {
        await _fs.WriteAsync(_id, "/notes.txt", Text("привет"), overwrite: false);

        Assert.Equal("привет", await ReadAllAsync(await _fs.OpenReadAsync(_id, "/notes.txt")));
        Assert.Equal("привет", File.ReadAllText(Disk("notes.txt")));
    }

    [Fact]
    public async Task Write_Existing_File_Without_Overwrite_Throws_IOException_And_Keeps_The_File()
    {
        File.WriteAllText(Disk("a.txt"), "old");

        var error = await Assert.ThrowsAsync<IOException>(() => _fs.WriteAsync(_id, "/a.txt", Text("new"), overwrite: false));

        Assert.Contains("/a.txt", error.Message);
        Assert.Contains("overwrite", error.Message);
        Assert.Equal("old", File.ReadAllText(Disk("a.txt")));
        Assert.Empty(_confirmation.Asked);
    }

    [Fact]
    public async Task Write_Existing_File_With_Overwrite_Replaces_The_Content()
    {
        File.WriteAllText(Disk("a.txt"), "old");

        await _fs.WriteAsync(_id, "/a.txt", Text("new"), overwrite: true);

        Assert.Equal("new", File.ReadAllText(Disk("a.txt")));
        Assert.Contains("перезаписать файл", Assert.Single(_confirmation.Asked));
    }

    [Fact]
    public async Task Write_Over_A_Folder_Throws_IOException()
    {
        Directory.CreateDirectory(Disk("dir"));

        await Assert.ThrowsAsync<IOException>(() => _fs.WriteAsync(_id, "/dir", Text("x"), overwrite: true));
    }

    [Fact]
    public async Task CreateDirectory_Creates_The_Folder_And_Refuses_An_Existing_Path()
    {
        await _fs.CreateDirectoryAsync(_id, "/docs");

        Assert.True(Directory.Exists(Disk("docs")));
        await Assert.ThrowsAsync<IOException>(() => _fs.CreateDirectoryAsync(_id, "/docs"));
        File.WriteAllText(Disk("f.txt"), "x");
        await Assert.ThrowsAsync<IOException>(() => _fs.CreateDirectoryAsync(_id, "/f.txt"));
    }

    [Fact]
    public async Task List_Returns_Entries_With_Posix_Paths()
    {
        Directory.CreateDirectory(Disk("docs/inner"));
        File.WriteAllText(Disk("docs/a.txt"), "12345");

        var entries = (await _fs.ListAsync(_id, "/docs/")).OrderBy(e => e.Name).ToList();

        Assert.Equal(["a.txt", "inner"], entries.Select(e => e.Name));
        Assert.Equal(["/docs/a.txt", "/docs/inner"], entries.Select(e => e.FullPath));
        Assert.Equal([false, true], entries.Select(e => e.IsDirectory));
        Assert.Equal(5, entries[0].Size);
        Assert.True(DateTimeOffset.UtcNow - entries[0].LastModified < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Stat_Describes_A_File_And_A_Folder()
    {
        Directory.CreateDirectory(Disk("docs"));
        File.WriteAllText(Disk("docs/a.txt"), "abc");

        var file = await _fs.StatAsync(_id, "/docs/a.txt");
        var folder = await _fs.StatAsync(_id, "/docs");

        Assert.Equal(("a.txt", "/docs/a.txt", false, 3), (file.Name, file.FullPath, file.IsDirectory, file.Size));
        Assert.Equal(("docs", "/docs", true), (folder.Name, folder.FullPath, folder.IsDirectory));
    }

    [Fact]
    public async Task Delete_File_Removes_It_With_Or_Without_Recursive()
    {
        File.WriteAllText(Disk("a.txt"), "x");
        File.WriteAllText(Disk("b.txt"), "x");

        await _fs.DeleteAsync(_id, "/a.txt", recursive: false);
        await _fs.DeleteAsync(_id, "/b.txt", recursive: true);

        Assert.False(File.Exists(Disk("a.txt")));
        Assert.False(File.Exists(Disk("b.txt")));
    }

    [Fact]
    public async Task Delete_Empty_Folder_Works_Without_Recursive()
    {
        Directory.CreateDirectory(Disk("empty"));

        await _fs.DeleteAsync(_id, "/empty", recursive: false);

        Assert.False(Directory.Exists(Disk("empty")));
    }

    [Fact]
    public async Task Delete_Not_Empty_Folder_Without_Recursive_Throws_IOException_And_Keeps_It()
    {
        Directory.CreateDirectory(Disk("full/inner"));
        File.WriteAllText(Disk("full/a.txt"), "x");

        await Assert.ThrowsAsync<IOException>(() => _fs.DeleteAsync(_id, "/full", recursive: false));

        Assert.True(File.Exists(Disk("full/a.txt")));
        Assert.Empty(_confirmation.Asked);
    }

    [Fact]
    public async Task Delete_Not_Empty_Folder_With_Recursive_Removes_Everything()
    {
        Directory.CreateDirectory(Disk("full/inner"));
        File.WriteAllText(Disk("full/inner/a.txt"), "x");

        await _fs.DeleteAsync(_id, "/full", recursive: true);

        Assert.False(Directory.Exists(Disk("full")));
        Assert.Contains("со всем содержимым", Assert.Single(_confirmation.Asked));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Move_File_Respects_Overwrite(bool overwrite)
    {
        File.WriteAllText(Disk("from.txt"), "source");
        File.WriteAllText(Disk("to.txt"), "target");

        if (overwrite)
        {
            await _fs.MoveAsync(_id, "/from.txt", "/to.txt", overwrite: true);
            Assert.False(File.Exists(Disk("from.txt")));
            Assert.Equal("source", File.ReadAllText(Disk("to.txt")));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => _fs.MoveAsync(_id, "/from.txt", "/to.txt", overwrite: false));
            Assert.Equal("source", File.ReadAllText(Disk("from.txt")));
            Assert.Equal("target", File.ReadAllText(Disk("to.txt")));
        }
    }

    [Fact]
    public async Task Move_To_A_New_Name_And_Move_A_Folder()
    {
        File.WriteAllText(Disk("a.txt"), "x");
        Directory.CreateDirectory(Disk("dir"));
        File.WriteAllText(Disk("dir/f.txt"), "y");

        await _fs.MoveAsync(_id, "/a.txt", "/b.txt", overwrite: false);
        await _fs.MoveAsync(_id, "/dir", "/renamed", overwrite: false);

        Assert.True(File.Exists(Disk("b.txt")));
        Assert.False(File.Exists(Disk("a.txt")));
        Assert.True(File.Exists(Disk("renamed/f.txt")));
        Assert.False(Directory.Exists(Disk("dir")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Copy_File_Respects_Overwrite(bool overwrite)
    {
        File.WriteAllText(Disk("from.txt"), "source");
        File.WriteAllText(Disk("to.txt"), "target");

        if (overwrite)
        {
            await _fs.CopyAsync(_id, "/from.txt", "/to.txt", overwrite: true);
            Assert.Equal("source", File.ReadAllText(Disk("to.txt")));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => _fs.CopyAsync(_id, "/from.txt", "/to.txt", overwrite: false));
            Assert.Equal("target", File.ReadAllText(Disk("to.txt")));
        }

        Assert.Equal("source", File.ReadAllText(Disk("from.txt")));
    }

    [Fact]
    public async Task Copy_Folder_Copies_The_Content_And_Refuses_An_Existing_Target_Or_Itself()
    {
        Directory.CreateDirectory(Disk("dir/inner"));
        File.WriteAllText(Disk("dir/inner/f.txt"), "y");
        Directory.CreateDirectory(Disk("other"));

        await _fs.CopyAsync(_id, "/dir", "/copy", overwrite: false);

        Assert.Equal("y", File.ReadAllText(Disk("copy/inner/f.txt")));
        Assert.True(File.Exists(Disk("dir/inner/f.txt")));
        await Assert.ThrowsAsync<IOException>(() => _fs.CopyAsync(_id, "/dir", "/other", overwrite: true));
        await Assert.ThrowsAsync<IOException>(() => _fs.CopyAsync(_id, "/dir", "/dir/inner/again", overwrite: false));
    }

    [Fact]
    public async Task Every_Change_Is_Confirmed_With_The_Plugin_Name_And_Reads_Are_Not()
    {
        File.WriteAllText(Disk("a.txt"), "x");

        await _fs.ListAsync(_id, "/");
        await _fs.StatAsync(_id, "/a.txt");
        await ReadAllAsync(await _fs.OpenReadAsync(_id, "/a.txt"));
        Assert.Empty(_confirmation.Asked);

        await _fs.WriteAsync(_id, "/b.txt", Text("x"), overwrite: false);
        await _fs.CreateDirectoryAsync(_id, "/d");
        await _fs.CopyAsync(_id, "/a.txt", "/c.txt", overwrite: false);
        await _fs.MoveAsync(_id, "/c.txt", "/e.txt", overwrite: false);
        await _fs.DeleteAsync(_id, "/e.txt", recursive: false);

        Assert.Equal(
        [
            "Test Plugin|записать файл|/b.txt|",
            "Test Plugin|создать папку|/d|",
            "Test Plugin|скопировать файл|/a.txt|/c.txt",
            "Test Plugin|переместить файл|/c.txt|/e.txt",
            "Test Plugin|удалить файл|/e.txt|",
        ], _confirmation.Asked);
    }

    [Fact]
    public async Task Missing_Paths_Throw_FsNotFound()
    {
        var missing = new Func<Task>[]
        {
            () => _fs.ListAsync(_id, "/nope"),
            () => _fs.StatAsync(_id, "/nope"),
            () => _fs.OpenReadAsync(_id, "/nope.txt"),
            () => _fs.DeleteAsync(_id, "/nope", recursive: true),
            () => _fs.MoveAsync(_id, "/nope", "/x", overwrite: true),
            () => _fs.CopyAsync(_id, "/nope", "/x", overwrite: true),
        };

        foreach (var call in missing)
        {
            var error = await Assert.ThrowsAsync<FsNotFoundException>(call);
            Assert.StartsWith("/nope", error.Path);
        }

        Assert.Empty(_confirmation.Asked);
    }

    [Fact]
    public async Task A_File_Is_Not_A_Folder_To_List_And_A_Folder_Is_Not_A_File_To_Read()
    {
        File.WriteAllText(Disk("a.txt"), "x");
        Directory.CreateDirectory(Disk("dir"));

        await Assert.ThrowsAsync<FsNotFoundException>(() => _fs.ListAsync(_id, "/a.txt"));
        await Assert.ThrowsAsync<FsNotFoundException>(() => _fs.OpenReadAsync(_id, "/dir"));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/../../etc/passwd")]
    public async Task A_Path_Outside_The_Root_Throws_FsAccessDenied_For_Reads_And_Changes(string path)
    {
        File.WriteAllText(Disk("ok.txt"), "x");

        await Assert.ThrowsAsync<FsAccessDeniedException>(() => _fs.StatAsync(_id, path));
        await Assert.ThrowsAsync<FsAccessDeniedException>(() => _fs.OpenReadAsync(_id, path));
        await Assert.ThrowsAsync<FsAccessDeniedException>(() => _fs.WriteAsync(_id, path, Text("x"), overwrite: true));
        await Assert.ThrowsAsync<FsAccessDeniedException>(() => _fs.DeleteAsync(_id, path, recursive: true));
        await Assert.ThrowsAsync<FsAccessDeniedException>(() => _fs.MoveAsync(_id, "/ok.txt", path, overwrite: true));
        await Assert.ThrowsAsync<FsAccessDeniedException>(() => _fs.CopyAsync(_id, path, "/ok2.txt", overwrite: true));

        // Rejected before the user was asked.
        Assert.Empty(_confirmation.Asked);
    }

    [Fact]
    public async Task Unknown_Connection_Throws_FsNotFound_Before_Any_Question()
    {
        var unknown = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<FsNotFoundException>(() => _fs.ListAsync(unknown, "/"));
        await Assert.ThrowsAsync<FsNotFoundException>(() => _fs.WriteAsync(unknown, "/a.txt", Text("x"), overwrite: false));
        await Assert.ThrowsAsync<FsNotFoundException>(() => _fs.DeleteAsync(unknown, "/a.txt", recursive: false));

        Assert.Contains(unknown.ToString(), error.Message);
        Assert.Empty(_confirmation.Asked);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), true)]
    [InlineData(typeof(SftpPermissionDeniedException), true)]
    [InlineData(typeof(AccessViolationException), true)]
    [InlineData(typeof(SftpPathNotFoundException), false)]
    [InlineData(typeof(FileNotFoundException), false)]
    [InlineData(typeof(DirectoryNotFoundException), false)]
    public async Task Core_And_Sftp_Exceptions_Are_Mapped(Type thrown, bool accessDenied)
    {
        var error = (Exception)Activator.CreateInstance(thrown, "secret-detail")!;
        var id = _manager.Add(new FaultyConnection(_root, error));

        var mapped = accessDenied
            ? await Assert.ThrowsAsync<FsAccessDeniedException>(() => _fs.StatAsync(id, "/a"))
            : (Exception)await Assert.ThrowsAsync<FsNotFoundException>(() => _fs.StatAsync(id, "/a"));

        Assert.Same(error, mapped.InnerException);
        Assert.DoesNotContain("secret-detail", mapped.Message);
    }

    [Fact]
    public async Task Cancellation_Is_Not_Wrapped()
    {
        File.WriteAllText(Disk("a.txt"), "x");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _fs.OpenReadAsync(_id, "/a.txt", cts.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a\0b")]
    public async Task Empty_Or_Malformed_Path_Throws_ArgumentException(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fs.StatAsync(_id, path));
        await Assert.ThrowsAsync<ArgumentException>(() => _fs.WriteAsync(_id, path, Text("x"), overwrite: true));
    }
}
