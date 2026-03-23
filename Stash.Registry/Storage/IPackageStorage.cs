using System;
using System.IO;
using System.Threading.Tasks;

namespace Stash.Registry.Storage;

/// <summary>
/// Defines the contract for package tarball storage backends.
/// </summary>
/// <remarks>
/// Two implementations exist: <see cref="FileSystemStorage"/> (active, stores tarballs on
/// local disk) and <see cref="S3Storage"/> (stub, not yet implemented). Tarballs are
/// addressed by the combination of package name and version string.
/// </remarks>
public interface IPackageStorage
{
    /// <summary>
    /// Persists a package tarball to the backing store.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string of the package.</param>
    /// <param name="tarball">A readable stream containing the <c>.tar.gz</c> archive to store.</param>
    Task StoreAsync(string packageName, string version, Stream tarball);

    /// <summary>
    /// Opens a readable stream for an existing tarball from the backing store.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string of the package.</param>
    /// <returns>
    /// A <see cref="Stream"/> over the tarball bytes, or <see langword="null"/> if no
    /// tarball exists for the given name and version.
    /// </returns>
    Task<Stream?> RetrieveAsync(string packageName, string version);

    /// <summary>
    /// Determines whether a tarball for the given package version exists in the backing store.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string of the package.</param>
    /// <returns>
    /// <see langword="true"/> if a tarball exists; otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> ExistsAsync(string packageName, string version);

    /// <summary>
    /// Removes the tarball for the given package version from the backing store.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string of the package.</param>
    /// <returns>
    /// <see langword="true"/> if the tarball was found and deleted; <see langword="false"/>
    /// if it did not exist.
    /// </returns>
    Task<bool> DeleteAsync(string packageName, string version);

    /// <summary>
    /// Returns the stored size in bytes of the tarball for the given package version.
    /// </summary>
    /// <param name="packageName">The name of the package.</param>
    /// <param name="version">The version string of the package.</param>
    /// <returns>
    /// The file size in bytes, or <c>0</c> if the tarball does not exist.
    /// </returns>
    Task<long> GetSizeAsync(string packageName, string version);
}
