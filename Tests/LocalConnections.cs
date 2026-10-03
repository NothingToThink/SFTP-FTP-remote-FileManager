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
}
