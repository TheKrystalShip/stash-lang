using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stash.Common;
using Stash.Registry.Auth;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Contracts;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Storage;

namespace Stash.Registry.Services;

/// <summary>
/// Encapsulates publish and unpublish business logic for the package registry.
/// </summary>
/// <remarks>
/// The service is route-authoritative: callers MUST supply the canonical
/// <see cref="PackageResource"/> derived from the HTTP route (not from the manifest body).
/// The service validates that the manifest's <c>name</c> and <c>version</c> fields
/// agree with the authoritative resource and intended version, rejecting mismatches
/// with <see cref="ManifestRouteMismatchException"/> (Bug B / Q5 closure).
/// </remarks>
public sealed class PackageService
{
    private readonly IRegistryDatabase _db;
    private readonly IPackageStorage _storage;
    private readonly RegistryConfig _config;
    private readonly ILogger<PackageService> _logger;

    public PackageService(IRegistryDatabase db, IPackageStorage storage, RegistryConfig config, ILogger<PackageService>? logger = null)
    {
        _db = db;
        _storage = storage;
        _config = config;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PackageService>.Instance;
    }

    /// <summary>
    /// Publishes a package version using an authoritative resource from the HTTP route.
    /// </summary>
    /// <param name="resource">
    /// The canonical package identity derived from the URL route via
    /// <see cref="PackageRoute.From"/>. The manifest's <c>name</c> field must agree;
    /// disagreement is rejected as <see cref="ManifestRouteMismatchException"/>.
    /// </param>
    /// <param name="tarball">The raw tarball stream from the request body.</param>
    /// <param name="principal">
    /// The calling principal.  On a new-package (CreatePackage) publish the caller
    /// is assigned as owner.  For version publishes (PublishVersion) the caller is
    /// recorded as published_by but no new role row is inserted.
    /// </param>
    /// <param name="clientIntegrity">Optional client-supplied <c>sha256-…</c> hash for integrity verification.</param>
    /// <param name="expectedVersion">
    /// Optional expected version from the request (e.g. <c>X-Package-Version</c> header).
    /// When supplied the manifest's <c>version</c> field must agree; disagreement is
    /// rejected as <see cref="ManifestRouteMismatchException"/> (Q5 closure).
    /// </param>
    /// <returns>
    /// A tuple of (versionRecord, isNewPackage) — <c>isNewPackage</c> is true when the
    /// package row was created by this publish (CreatePackage semantics).
    /// </returns>
    public async Task<(VersionRecord vr, bool isNewPackage)> PublishAsync(
        PackageResource resource,
        Stream tarball,
        Principal principal,
        string? clientIntegrity,
        string? expectedVersion = null)
    {
        string username = principal is UserPrincipal up ? up.Username
            : throw new InvalidOperationException("PublishAsync requires an authenticated principal.");

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

        // ── Route-authoritative identity check (Bug B / D3 closure) ──────────
        //
        // The route is canonical. The manifest is a redundancy check only.
        string expectedFullName = resource.FullName; // @scope/localName
        string? manifestName = manifest.Name;

        if (!string.Equals(manifestName, expectedFullName, StringComparison.Ordinal))
        {
            throw new ManifestRouteMismatchException(
                $"Manifest name '{manifestName}' does not match route resource '{expectedFullName}'. " +
                "The route is the authoritative package identity.",
                field: "name",
                expected: expectedFullName,
                actual: manifestName ?? "(null)");
        }

        // ── Version mismatch check (Q5 closure) ──────────────────────────────
        //
        // If the caller supplied an expected version (e.g. via X-Package-Version header)
        // the manifest's version must agree.
        string manifestVersion = manifest.Version!;
        if (expectedVersion != null &&
            !string.Equals(manifestVersion, expectedVersion, StringComparison.Ordinal))
        {
            throw new ManifestRouteMismatchException(
                $"Manifest version '{manifestVersion}' does not match expected version '{expectedVersion}'.",
                field: "version",
                expected: expectedVersion,
                actual: manifestVersion);
        }

        string integrity = ComputeIntegrity(tarballBytes);

        if (clientIntegrity != null && !string.Equals(clientIntegrity, integrity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Integrity check failed: tarball hash does not match client-provided integrity.");
        }

        string packageName = resource.FullName;
        string version = manifestVersion;

        if (await _db.VersionExistsAsync(packageName, version))
        {
            throw new VersionConflictException(packageName, version);
        }

        // ── Insert-then-handle-unique-violation (D20 / atomic CreatePackage) ──
        //
        // We do NOT check-then-act.  Issue the INSERT; on a unique-constraint violation
        // (PK collision on the packages.name column) the loser falls through to the
        // PublishVersion code path on the now-existing row.
        bool isNewPackage = false;
        bool packageExisted = await _db.PackageExistsAsync(packageName);

        if (!packageExisted)
        {
            string? keywordsJson = manifest.Keywords != null
                ? JsonSerializer.Serialize(manifest.Keywords)
                : null;

            var packageRecord = new PackageRecord
            {
                Name = packageName,
                Description = manifest.Description,
                License = manifest.License,
                Repository = manifest.Repository,
                Keywords = keywordsJson,
                Latest = version,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            bool created = await _db.TryCreatePackageAsync(packageRecord);
            if (created)
            {
                await _db.AssignPackageRoleAsync(packageName, PrincipalTypes.User.ToWire(), username, PackageRoles.Owner.ToWire());
                isNewPackage = true;
            }
            else
            {
                // Lost the create race — another concurrent publish won.
                // Fall through to PublishVersion semantics on the existing row.
                _logger.LogDebug(
                    "CreatePackage lost race for '{PackageName}': collapsing to PublishVersion.",
                    packageName);
                isNewPackage = false;
            }
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
            PublishedBy = username,
            StorageBytes = tarballBytes.LongLength
        };

        await _db.AddVersionAsync(packageName, versionRecord);

        string? readme = ExtractFileFromTarball(tarballBytes, "README.md");
        if (readme != null)
        {
            await _db.UpdatePackageReadmeAsync(packageName, readme);
        }

        List<string> allVersions = await _db.GetAllVersionsAsync(packageName);
        string? newLatest = SelectLatestVersion(allVersions);
        if (newLatest is not null)
        {
            await _db.UpdatePackageLatestAsync(packageName, newLatest);
        }

        using var storeStream = new MemoryStream(tarballBytes);
        await _storage.StoreAsync(packageName, version, storeStream);

        return (versionRecord, isNewPackage);
    }

    /// <summary>
    /// Unpublishes a version.  The PDP governs who may call this; the service
    /// no longer performs its own owner check.
    /// </summary>
    public async Task<bool> UnpublishAsync(string name, string version, string username)
    {
        PackageRecord package = await _db.GetPackageAsync(name)
            ?? throw new InvalidOperationException($"Package '{name}' not found.");

        VersionRecord versionRecord = await _db.GetPackageVersionAsync(name, version)
            ?? throw new InvalidOperationException($"Version {version} of '{name}' not found.");

        TimeSpan window = _config.Security.UnpublishWindowTimeSpan;
        if (DateTime.UtcNow - versionRecord.PublishedAt > window)
        {
            throw new InvalidOperationException(
                $"Unpublish window ({_config.Security.UnpublishWindow}) has expired for version {version}.");
        }

        await _storage.DeleteAsync(name, version);
        await _db.DeleteVersionAsync(name, version);

        List<string> remaining = await _db.GetAllVersionsAsync(name);
        if (remaining.Count > 0)
        {
            string? newLatest = SelectLatestVersion(remaining);
            if (newLatest is not null)
            {
                await _db.UpdatePackageLatestAsync(name, newLatest);
            }
        }

        return true;
    }

    /// <summary>
    /// Selects the highest-precedence version from the provided list using semver 2.0 ordering.
    /// Stable releases always outrank prereleases. Versions that fail to parse are ignored.
    /// </summary>
    private static string? SelectLatestVersion(IEnumerable<string> versions)
    {
        SemVer? bestStable = null;
        string? bestStableStr = null;
        SemVer? bestPre = null;
        string? bestPreStr = null;

        foreach (var v in versions)
        {
            if (!SemVer.TryParse(v, out var parsed) || parsed is null) continue;

            if (parsed.IsPreRelease)
            {
                if (bestPre is null || parsed.CompareTo(bestPre) > 0)
                {
                    bestPre = parsed;
                    bestPreStr = v;
                }
            }
            else
            {
                if (bestStable is null || parsed.CompareTo(bestStable) > 0)
                {
                    bestStable = parsed;
                    bestStableStr = v;
                }
            }
        }

        return bestStableStr ?? bestPreStr;
    }

    // ── Backward-compat helper for unit tests that pre-date P3 ──────────────
    //
    // These tests were written against the old Publish(Stream, string, string?)
    // signature.  The overload constructs an authoritative PackageResource from
    // the manifest name so the route-mismatch check is trivially satisfied,
    // preserving the test contract while exercising the real PublishAsync path.
    //
    // NOTE: this overload exists ONLY so existing unit tests compile without
    // a full rewrite.  New callers MUST use PublishAsync(resource, …).
    /// <summary>
    /// Backward-compat wrapper.  Parses the manifest to build an authoritative
    /// resource from its name, then calls <see cref="PublishAsync"/>.
    /// Preserved for pre-P3 unit tests only.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public async Task<VersionRecord> Publish(Stream tarball, string username, string? clientIntegrity)
    {
        // Peek at the manifest to extract the name for routing
        using var peekMs = new MemoryStream();
        await tarball.CopyToAsync(peekMs);
        byte[] tarballBytes = peekMs.ToArray();

        string? manifestJson = ExtractFileFromTarball(tarballBytes, "stash.json");
        if (manifestJson == null)
            throw new InvalidOperationException("Package tarball must contain a stash.json manifest.");

        var manifest = System.Text.Json.JsonSerializer.Deserialize<Stash.Common.PackageManifest>(manifestJson)
            ?? throw new InvalidOperationException("Failed to parse stash.json manifest.");

        if (string.IsNullOrEmpty(manifest.Name))
            throw new InvalidOperationException("Invalid package manifest: name is required.");

        // Split "@scope/name" to build a PackageResource
        Stash.Common.PackageManifest.SplitScopedName(manifest.Name);  // validates format
        int slash = manifest.Name.IndexOf('/');
        string scopePart = manifest.Name[1..slash]; // strip leading @
        string namePart = manifest.Name[(slash + 1)..];
        var resource = new Auth.Authorization.PackageResource(scopePart, namePart);

        var principal = new Auth.Authorization.UserPrincipal(
            username, Auth.Authorization.UserRole.User,
            Auth.Authorization.TokenCeiling.Publish, "legacy-tok");

        // Seed scope so AuthorizeCreatePackageAsync doesn't deny
        if (!await _db.ScopeExistsAsync(scopePart))
        {
            await _db.CreateScopeAsync(new Database.Models.ScopeRecord
            {
                Name = scopePart,
                OwnerType = ScopeOwnerTypes.User,
                OwnerUsername = username
            });
        }

        var (vr, _) = await PublishAsync(resource, new MemoryStream(tarballBytes), principal, clientIntegrity);
        return vr;
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

/// <summary>
/// Thrown when the manifest's <c>name</c> or <c>version</c> field disagrees with
/// the authoritative identity derived from the HTTP route.  Maps to HTTP 400 at
/// the controller layer.
/// </summary>
public sealed class ManifestRouteMismatchException : InvalidOperationException
{
    /// <summary>The manifest field that mismatched: <c>"name"</c> or <c>"version"</c>.</summary>
    public string Field { get; }

    /// <summary>The expected value (from the route / header).</summary>
    public string Expected { get; }

    /// <summary>The actual value found in the manifest.</summary>
    public string Actual { get; }

    public ManifestRouteMismatchException(string message, string field, string expected, string actual)
        : base(message)
    {
        Field = field;
        Expected = expected;
        Actual = actual;
    }
}
