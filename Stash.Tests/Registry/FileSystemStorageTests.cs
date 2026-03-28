using System.Text;
using Stash.Registry.Storage;

namespace Stash.Tests.Registry;

public sealed class FileSystemStorageTests : IDisposable
{
    private readonly string _rootDir;
    private readonly FileSystemStorage _storage;

    public FileSystemStorageTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), $"stash-storage-test-{Guid.NewGuid():N}");
        _storage = new FileSystemStorage(_rootDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_rootDir, true); } catch { }
    }

    private static MemoryStream StreamFrom(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task Store_Retrieve_RoundTrips()
    {
        using var input = StreamFrom("hello tarball");
        await _storage.StoreAsync("my-pkg", "1.0.0", input);

        using Stream? result = await _storage.RetrieveAsync("my-pkg", "1.0.0");

        Assert.NotNull(result);
        using var reader = new StreamReader(result);
        Assert.Equal("hello tarball", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Store_Exists_ReturnsTrue()
    {
        using var input = StreamFrom("data");
        await _storage.StoreAsync("exist-pkg", "1.0.0", input);

        Assert.True(await _storage.ExistsAsync("exist-pkg", "1.0.0"));
    }

    [Fact]
    public async Task Exists_NonExistent_ReturnsFalse()
    {
        Assert.False(await _storage.ExistsAsync("no-pkg", "0.0.1"));
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        using var input = StreamFrom("to-delete");
        await _storage.StoreAsync("del-pkg", "1.0.0", input);
        Assert.True(await _storage.ExistsAsync("del-pkg", "1.0.0"));

        bool deleted = await _storage.DeleteAsync("del-pkg", "1.0.0");

        Assert.True(deleted);
        Assert.False(await _storage.ExistsAsync("del-pkg", "1.0.0"));
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsFalse()
    {
        bool result = await _storage.DeleteAsync("ghost-pkg", "1.0.0");

        Assert.False(result);
    }

    [Fact]
    public async Task GetSize_ReturnsFileSize()
    {
        byte[] data = Encoding.UTF8.GetBytes("some content here");
        using var input = new MemoryStream(data);
        await _storage.StoreAsync("size-pkg", "1.0.0", input);

        long size = await _storage.GetSizeAsync("size-pkg", "1.0.0");

        Assert.Equal(data.Length, size);
    }

    [Fact]
    public async Task GetSize_NonExistent_ReturnsZero()
    {
        long size = await _storage.GetSizeAsync("missing", "1.0.0");

        Assert.Equal(0, size);
    }

    [Fact]
    public async Task Store_ScopedPackage_HandlesSlash()
    {
        using var input = StreamFrom("scoped content");
        await _storage.StoreAsync("@scope/my-pkg", "2.0.0", input);

        Assert.True(await _storage.ExistsAsync("@scope/my-pkg", "2.0.0"));

        using Stream? result = await _storage.RetrieveAsync("@scope/my-pkg", "2.0.0");
        Assert.NotNull(result);
        using var reader = new StreamReader(result);
        Assert.Equal("scoped content", await reader.ReadToEndAsync());
    }
}
