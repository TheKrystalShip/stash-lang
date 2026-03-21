using System;
using System.IO;
using System.Threading.Tasks;

namespace Stash.Registry.Storage;

public sealed class S3Storage : IPackageStorage
{
    private readonly string _bucket;
    private readonly string _region;
    private readonly string _endpoint;
    private readonly string _accessKey;
    private readonly string _secretKey;

    public S3Storage(string bucket, string region, string? endpoint, string accessKey, string secretKey)
    {
        _bucket = bucket;
        _region = region;
        _endpoint = endpoint ?? $"https://s3.{region}.amazonaws.com";
        _accessKey = accessKey;
        _secretKey = secretKey;
    }

    public Task StoreAsync(string packageName, string version, Stream tarball)
    {
        throw new NotSupportedException("S3 storage is not yet implemented. Use filesystem storage.");
    }

    public Task<Stream?> RetrieveAsync(string packageName, string version)
    {
        throw new NotSupportedException("S3 storage is not yet implemented. Use filesystem storage.");
    }

    public Task<bool> ExistsAsync(string packageName, string version)
    {
        throw new NotSupportedException("S3 storage is not yet implemented. Use filesystem storage.");
    }

    public Task<bool> DeleteAsync(string packageName, string version)
    {
        throw new NotSupportedException("S3 storage is not yet implemented. Use filesystem storage.");
    }

    public Task<long> GetSizeAsync(string packageName, string version)
    {
        throw new NotSupportedException("S3 storage is not yet implemented. Use filesystem storage.");
    }

    private string GetObjectKey(string packageName, string version)
    {
        string safeName = packageName.Replace('/', '_');
        return $"{safeName}/{version}.tar.gz";
    }
}
