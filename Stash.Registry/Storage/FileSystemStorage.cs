using System;
using System.IO;
using System.Threading.Tasks;

namespace Stash.Registry.Storage;

public sealed class FileSystemStorage : IPackageStorage
{
    private readonly string _rootDir;

    public FileSystemStorage(string rootDir)
    {
        _rootDir = rootDir;
        Directory.CreateDirectory(_rootDir);
    }

    public async Task StoreAsync(string packageName, string version, Stream tarball)
    {
        string path = GetPath(packageName, version);
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
        await tarball.CopyToAsync(fileStream);
    }

    public Task<Stream?> RetrieveAsync(string packageName, string version)
    {
        string path = GetPath(packageName, version);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read));
    }

    public Task<bool> ExistsAsync(string packageName, string version)
    {
        return Task.FromResult(File.Exists(GetPath(packageName, version)));
    }

    public Task<bool> DeleteAsync(string packageName, string version)
    {
        string path = GetPath(packageName, version);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    public Task<long> GetSizeAsync(string packageName, string version)
    {
        string path = GetPath(packageName, version);
        if (!File.Exists(path))
        {
            return Task.FromResult(0L);
        }

        return Task.FromResult(new FileInfo(path).Length);
    }

    private string GetPath(string packageName, string version)
    {
        string safeName = packageName.Replace('/', '_').Replace('\\', '_');
        string safeVersion = version.Replace('/', '_').Replace('\\', '_');

        // Remove any path traversal sequences
        safeName = safeName.Replace("..", "__");
        safeVersion = safeVersion.Replace("..", "__");

        string path = Path.GetFullPath(Path.Combine(_rootDir, safeName, $"{safeVersion}.tar.gz"));

        // Ensure path stays within root directory
        string normalizedRoot = Path.GetFullPath(_rootDir);
        if (!path.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid package name or version: path traversal detected.");
        }

        return path;
    }
}
