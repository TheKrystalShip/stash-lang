using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Storage;

namespace Stash.Registry.Services;

public sealed class PackageService
{
    private readonly IRegistryDatabase _db;
    private readonly IPackageStorage _storage;
    private readonly RegistryConfig _config;

    public PackageService(IRegistryDatabase db, IPackageStorage storage, RegistryConfig config)
    {
        _db = db;
        _storage = storage;
        _config = config;
    }

    public async Task<VersionRecord> Publish(Stream tarball, string username, string? clientIntegrity)
    {
        using var memoryStream = new MemoryStream();
        await tarball.CopyToAsync(memoryStream);
        byte[] tarballBytes = memoryStream.ToArray();

        if (tarballBytes.Length > _config.Security.MaxPackageSizeBytes)
        {
            throw new InvalidOperationException(
                $"Package exceeds maximum size of {_config.Security.MaxPackageSize}.");
        }

        string? manifestJson = ExtractFileFromTarball(tarballBytes, "stash.json");
        if (manifestJson == null)
        {
            throw new InvalidOperationException("Package tarball must contain a stash.json manifest.");
        }

        PackageManifest manifest = JsonSerializer.Deserialize<PackageManifest>(manifestJson)
            ?? throw new InvalidOperationException("Failed to parse stash.json manifest.");

        List<string> errors = manifest.ValidateForPublishing();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid package manifest: " + string.Join("; ", errors));
        }

        if (!HasStashFile(tarballBytes))
        {
            throw new InvalidOperationException("Package must contain at least one .stash file.");
        }

        string integrity = ComputeIntegrity(tarballBytes);

        if (clientIntegrity != null && !string.Equals(clientIntegrity, integrity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Integrity check failed: tarball hash does not match client-provided integrity.");
        }

        string packageName = manifest.Name!;
        string version = manifest.Version!;

        if (await _db.VersionExistsAsync(packageName, version))
        {
            throw new InvalidOperationException(
                $"Version {version} of '{packageName}' already exists. Versions are immutable.");
        }

        bool isNewPackage = !await _db.PackageExistsAsync(packageName);

        if (isNewPackage)
        {
            string? keywordsJson = manifest.Keywords != null
                ? JsonSerializer.Serialize(manifest.Keywords)
                : null;

            await _db.CreatePackageAsync(new PackageRecord
            {
                Name = packageName,
                Description = manifest.Description,
                License = manifest.License,
                Repository = manifest.Repository,
                Keywords = keywordsJson,
                Latest = version,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await _db.AddOwnerAsync(packageName, username);
        }

        string? dependenciesJson = manifest.Dependencies != null
            ? JsonSerializer.Serialize(manifest.Dependencies)
            : null;

        var versionRecord = new VersionRecord
        {
            PackageName = packageName,
            Version = version,
            StashVersion = manifest.Stash,
            Dependencies = dependenciesJson,
            Integrity = integrity,
            PublishedAt = DateTime.UtcNow,
            PublishedBy = username
        };

        await _db.AddVersionAsync(packageName, versionRecord);

        string? readme = ExtractFileFromTarball(tarballBytes, "README.md");
        if (readme != null)
        {
            await _db.UpdatePackageReadmeAsync(packageName, readme);
        }

        if (!isNewPackage)
        {
            await _db.UpdatePackageLatestAsync(packageName, version);
        }

        using var storeStream = new MemoryStream(tarballBytes);
        await _storage.StoreAsync(packageName, version, storeStream);

        return versionRecord;
    }

    public async Task<bool> UnpublishAsync(string name, string version, string username)
    {
        PackageRecord package = await _db.GetPackageAsync(name)
            ?? throw new InvalidOperationException($"Package '{name}' not found.");

        VersionRecord versionRecord = await _db.GetPackageVersionAsync(name, version)
            ?? throw new InvalidOperationException($"Version {version} of '{name}' not found.");

        if (!await _db.IsOwnerAsync(name, username))
        {
            throw new UnauthorizedAccessException($"User '{username}' is not an owner of '{name}'.");
        }

        TimeSpan window = _config.Security.UnpublishWindowTimeSpan;
        if (DateTime.UtcNow - versionRecord.PublishedAt > window)
        {
            throw new InvalidOperationException(
                $"Unpublish window ({_config.Security.UnpublishWindow}) has expired for version {version}.");
        }

        await _storage.DeleteAsync(name, version);
        await _db.DeleteVersionAsync(name, version);

        List<string> remaining = await _db.GetAllVersionsAsync(name);
        if (remaining.Count == 0)
        {
            // No versions remain — could clean up package record in the future
        }
        else
        {
            await _db.UpdatePackageLatestAsync(name, remaining[0]);
        }

        return true;
    }

    public async Task<PackageRecord?> GetPackageAsync(string name)
    {
        return await _db.GetPackageAsync(name);
    }

    public async Task<VersionRecord?> GetVersionAsync(string name, string version)
    {
        return await _db.GetPackageVersionAsync(name, version);
    }

    public async Task<Stream?> DownloadPackageAsync(string name, string version)
    {
        return await _storage.RetrieveAsync(name, version);
    }

    private static string? ExtractFileFromTarball(byte[] tarballBytes, string filename)
    {
        using var ms = new MemoryStream(tarballBytes);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var tar = new TarReader(gz);

        while (tar.GetNextEntry() is TarEntry entry)
        {
            string name = entry.Name.Replace('\\', '/');
            // Handle both "stash.json" and "package/stash.json" patterns
            if (name == filename || name.EndsWith("/" + filename, StringComparison.Ordinal))
            {
                if (entry.DataStream != null)
                {
                    using var reader = new StreamReader(entry.DataStream);
                    return reader.ReadToEnd();
                }
            }
        }

        return null;
    }

    private static bool HasStashFile(byte[] tarballBytes)
    {
        using var ms = new MemoryStream(tarballBytes);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var tar = new TarReader(gz);

        while (tar.GetNextEntry() is TarEntry entry)
        {
            string name = entry.Name.Replace('\\', '/');
            if (name.EndsWith(".stash", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeIntegrity(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return "sha256-" + Convert.ToBase64String(hash);
    }
}
