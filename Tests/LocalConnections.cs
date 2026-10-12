using Core.Implementations.Protocol;
using Xunit;

namespace Tests;

public class LocalConnectionTests
{
    [Fact]
    public async Task CreateFileAsync_ThenFileExists_ReturnsTrue()
    {
        DirectoryInfo tempFolder = Directory.CreateTempSubdirectory();
        LocalConnection conn = new LocalConnection(tempFolder.FullName);
        await conn.CreateFileAsync("test.txt");
        Assert.True(await conn.FileExistsAsync("test.txt"));
    }

    [Fact]
    public async Task DeleteFileAsync_ThenFileNotExists_ReturnArgEx() 
    {
        DirectoryInfo tempFolder = Directory.CreateTempSubdirectory();
        LocalConnection conn = new LocalConnection(tempFolder.FullName);
        await Assert.ThrowsAsync<ArgumentException>(() => conn.DeleteFileAsync("doesntexist.txt"));
    }


    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../../etc/passwd")]
    public async Task PathOutsideTheRoot_ThenThrowsArgEx(string path)
    {
        DirectoryInfo tempfolder = Directory.CreateTempSubdirectory();
        LocalConnection conn = new LocalConnection(tempfolder.FullName);
        await Assert.ThrowsAsync<AccessViolationException>(()
                => conn.FileExistsAsync(path));
    }

    [Fact]
    public async Task PathInSiblingFolderWithSamePrefix_ThenThrowsAccessViolationException()
    {
        DirectoryInfo parentFolder = Directory.CreateTempSubdirectory();
        string rootPath = Path.Combine(parentFolder.FullName, "sandbox");
        string siblingPath = Path.Combine(parentFolder.FullName, "sandbox-sibling");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(siblingPath);
        LocalConnection conn = new LocalConnection(rootPath);

        await Assert.ThrowsAsync<AccessViolationException>(
            () => conn.FileExistsAsync("../sandbox-sibling/secret.txt"));
    }

    [Fact]
    public async Task PathAtRootOrNestedInRoot_IsAllowed()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory();
        string nestedDirectory = Path.Combine(root.FullName, "nested");
        Directory.CreateDirectory(nestedDirectory);
        await File.WriteAllTextAsync(Path.Combine(nestedDirectory, "file.txt"), "content");
        LocalConnection conn = new LocalConnection(root.FullName);

        Assert.True(await conn.DirExistsAsync(""));
        Assert.True(await conn.FileExistsAsync("nested/file.txt"));
    }
}
