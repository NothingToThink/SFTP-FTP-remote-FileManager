using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Interfaces.Protocol;
using Core.Models;

namespace Tests.Fakes;

public class FakeConnection : Connection
{
    private readonly Dictionary<string, byte[]> _files = new();
    private readonly HashSet<string> _dirs = ["/"];
    private string _workingDirectory = "/";
    private bool _isConnected;

    public override bool IsConnected => _isConnected;

    public override void Connect() => _isConnected = true;
    public override void Disconnect() => _isConnected = false;

    public override void CreateFile(string remotePath) =>
        _files[remotePath] = Array.Empty<byte>();

    public override void DeleteFile(string remotePath) =>
        _files.Remove(remotePath);

    public override bool FileExists(string path) =>
        _files.ContainsKey(path);

    public override void RenameFile(string oldName, string newName)
    {
        _files[newName] = _files[oldName];
        _files.Remove(oldName);
    }

    public override void CopyFile(string sourcePath, string targetPath, bool canOverride = true)
    {
        if (!canOverride && _files.ContainsKey(targetPath))
            throw new IOException("File already exists");
        _files[targetPath] = _files[sourcePath].ToArray();
    }

    public override void MoveFile(string sourcePath, string targetPath, bool canOverride = true)
    {
        CopyFile(sourcePath, targetPath, canOverride);
        _files.Remove(sourcePath);
    }

    public override void SaveFile(string remotePath, Stream content)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        _files[remotePath] = ms.ToArray();
    }

    public override Stream GetFile(string path) =>
        new MemoryStream(_files[path]);

    public override List<FileItem> GetFiles(string path) =>
        _files.Keys
            .Where(f => f.StartsWith(path))
            .Select(f => new FileItem
            {
                Name = Path.GetFileName(f),
                FullPath = f,
                Size = _files[f].Length,
                LastModified = DateTime.Now,
                IsDirectory = false
            })
            .Concat(_dirs
                .Where(d => d.StartsWith(path) && d != path)
                .Select(d => new FileItem
                {
                    Name = Path.GetFileName(d.TrimEnd('/')),
                    FullPath = d,
                    Size = 0,
                    LastModified = DateTime.Now,
                    IsDirectory = true
                }))
            .ToList();

    public override FileItem GetInfo(string path) =>
        _files.ContainsKey(path)
            ? new FileItem
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                Size = _files[path].Length,
                LastModified = DateTime.Now,
                IsDirectory = false
            }
            : new FileItem
            {
                Name = Path.GetFileName(path.TrimEnd('/')),
                FullPath = path,
                Size = 0,
                LastModified = DateTime.Now,
                IsDirectory = _dirs.Contains(path)
            };
    
    public override void CreateDir(string remotePath) =>
        _dirs.Add(remotePath);

    public override void DeleteDir(string remotePath) =>
        _dirs.Remove(remotePath);

    public override bool DirExists(string path) =>
        _dirs.Contains(path);

    public override void RenameDir(string oldName, string newName)
    {
        _dirs.Remove(oldName);
        _dirs.Add(newName);
    }

    public override void ChangeDirectory(string path)
    {
        if (!_dirs.Contains(path))
            throw new DirectoryNotFoundException($"Directory {path} not found");
        _workingDirectory = path;
    }

    public override string GetWorkingDirectory() => _workingDirectory;

    public override List<string> GetDirectories(string path) =>
        _dirs.Where(d => d.StartsWith(path) && d != path).ToList();

    protected override void DisposeCore()
    {
        _files.Clear();
        _dirs.Clear();
        _isConnected = false;
    }
}