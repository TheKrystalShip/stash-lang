using System;
using System.IO;
using System.Threading.Tasks;

namespace Stash.Registry.Storage;

public interface IPackageStorage
{
    Task StoreAsync(string packageName, string version, Stream tarball);
    Task<Stream?> RetrieveAsync(string packageName, string version);
    Task<bool> ExistsAsync(string packageName, string version);
    Task<bool> DeleteAsync(string packageName, string version);
    Task<long> GetSizeAsync(string packageName, string version);
}
