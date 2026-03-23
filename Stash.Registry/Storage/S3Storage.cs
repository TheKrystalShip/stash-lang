using System;
using System.IO;
using System.Threading.Tasks;

namespace Stash.Registry.Storage;

/// <summary>
/// Stub <see cref="IPackageStorage"/> implementation for AWS S3-backed tarball storage.
/// </summary>
/// <remarks>
/// <para>
/// This class is not yet implemented. All interface methods throw
/// <see cref="NotSupportedException"/>. Use <see cref="FileSystemStorage"/> for active
/// deployments until S3 support is completed.
/// </para>
/// <para>
/// When implemented, tarballs will be stored in an S3 bucket under the object key
/// <c>{packageName}/{version}.tar.gz</c> (with slashes in the package name replaced by
/// underscores). Connectivity is configured via the <paramref name="bucket"/>,
/// <paramref name="region"/>, optional custom <paramref name="endpoint"/>,
/// <paramref name="accessKey"/>, and <paramref name="secretKey"/> constructor parameters.
/// </para>
/// </remarks>
public sealed class S3Storage : IPackageStorage
{
    /// <summary>
    /// The name of the S3 bucket used to store package tarballs.
    /// </summary>
    private readonly string _bucket;

    /// <summary>
    /// The AWS region in which the S3 bucket resides (e.g. <c>us-east-1</c>).
    /// </summary>
    private readonly string _region;

    /// <summary>
    /// The S3 endpoint URL. Defaults to <c>https://s3.{region}.amazonaws.com</c> when
    /// no custom endpoint is provided. A custom endpoint enables compatibility with
    /// S3-compatible object stores such as MinIO.
    /// </summary>
    private readonly string _endpoint;

    /// <summary>
    /// The AWS access key ID used for request signing.
    /// </summary>
    private readonly string _accessKey;

    /// <summary>
    /// The AWS secret access key used for request signing.
    /// </summary>
    private readonly string _secretKey;

    /// <summary>
    /// Initialises a new instance of <see cref="S3Storage"/> with the given AWS credentials
    /// and bucket configuration.
    /// </summary>
    /// <param name="bucket">The target S3 bucket name.</param>
    /// <param name="region">The AWS region (e.g. <c>us-east-1</c>).</param>
    /// <param name="endpoint">
    /// An optional custom endpoint URL. When <see langword="null"/>, defaults to
    /// <c>https://s3.{region}.amazonaws.com</c>.
    /// </param>
    /// <param name="accessKey">The AWS access key ID.</param>
    /// <param name="secretKey">The AWS secret access key.</param>
    public S3Storage(string bucket, string region, string? endpoint, string accessKey, string secretKey)
    {
        _bucket = bucket;
        _region = region;
        _endpoint = endpoint ?? $"https://s3.{region}.amazonaws.com";
        _accessKey = accessKey;
        _secretKey = secretKey;
    }

    /// <summary>
    /// Not implemented. Throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task StoreAsync(string packageName, string version, Stream tarball)
    {
        throw new NotSupportedException("S3 storage is not yet implemented. Use filesystem storage.");
    }

    /// <summary>
    /// Not implemented. Throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task<Stream?> RetrieveAsync(string packageName, string version)
    {
        throw new NotSupportedException("S3 storage is not yet implemented. Use filesystem storage.");
    }

    /// <summary>
    /// Not implemented. Throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task<bool> ExistsAsync(string packageName, string version)
    {
        throw new NotSupportedException("S3 storage is not yet implemented. Use filesystem storage.");
    }

    /// <summary>
    /// Not implemented. Throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task<bool> DeleteAsync(string packageName, string version)
    {
        throw new NotSupportedException("S3 storage is not yet implemented. Use filesystem storage.");
    }

    /// <summary>
    /// Not implemented. Throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task<long> GetSizeAsync(string packageName, string version)
    {
        throw new NotSupportedException("S3 storage is not yet implemented. Use filesystem storage.");
    }

    /// <summary>
    /// Constructs the S3 object key for a given package version.
    /// </summary>
    /// <remarks>
    /// Forward slashes in the package name are replaced with underscores so that the
    /// name component does not accidentally create additional S3 key path segments.
    /// The resulting key has the form <c>{safeName}/{version}.tar.gz</c>.
    /// </remarks>
    /// <param name="packageName">The raw package name.</param>
    /// <param name="version">The version string of the package.</param>
    /// <returns>The S3 object key string.</returns>
    private string GetObjectKey(string packageName, string version)
    {
        string safeName = packageName.Replace('/', '_');
        return $"{safeName}/{version}.tar.gz";
    }
}
