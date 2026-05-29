using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using Stash.Registry.Storage;

namespace Stash.Registry.Controllers;

/// <summary>
/// REST API controller for package operations (get, publish, unpublish, download).
/// </summary>
/// <remarks>
/// <para>
/// Public read endpoints (<c>GET</c>) require no authentication. Publishing and
/// unpublishing require a JWT with the <c>publish</c> or <c>admin</c> scope
/// (enforced by the <c>RequirePublishScope</c> policy). Ownership checks are
/// performed against the <see cref="IRegistryDatabase"/> ownership table before
/// any write is accepted.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/packages")]
public class PackagesController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IPackageStorage _storage;
    private readonly PackageService _packageService;
    private readonly AuditService _auditService;
    private readonly DeprecationService _deprecationService;
    private readonly RegistryConfig _config;

    /// <summary>
    /// Initialises the controller with its required services.
    /// </summary>
    /// <param name="db">Registry database for metadata queries and ownership checks.</param>
    /// <param name="storage">Blob storage backend for package tarballs.</param>
    /// <param name="packageService">Service that encapsulates publish and unpublish logic.</param>
    /// <param name="auditService">Service that records package-related audit events.</param>
    /// <param name="deprecationService">Service that handles package and version deprecation.</param>
    /// <param name="config">Registry-wide configuration.</param>
    public PackagesController(
        IRegistryDatabase db,
        IPackageStorage storage,
        PackageService packageService,
        AuditService auditService,
        DeprecationService deprecationService,
        RegistryConfig config)
    {
        _db = db;
        _storage = storage;
        _packageService = packageService;
        _auditService = auditService;
        _deprecationService = deprecationService;
        _config = config;
    }

    /// <summary>
    /// Gets package metadata including all published versions.
    /// </summary>
    /// <param name="name">The URL-encoded package name.</param>
    /// <remarks>
    /// No authentication required for public packages. Private/internal packages return 404
    /// to unauthenticated callers or callers without reader permission plus a read-scoped JWT.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="PackageDetailResponse"/> containing package info,
    /// owner list, and a version map, or <c>404</c> if the package does not exist or is not visible.
    /// </returns>
    [AllowAnonymous]
    [HttpGet("{name}")]
    public async Task<IActionResult> GetPackage(string name)
    {
        string decodedName = Uri.UnescapeDataString(name);
        PackageRecord? package = await _db.GetPackageAsync(decodedName);
        if (package == null)
        {
            return NotFound(new ErrorResponse { Error = $"Package '{decodedName}' not found." });
        }

        if (!await CanReadPackageAsync(package))
        {
            return NotFound(new ErrorResponse { Error = $"Package '{decodedName}' not found." });
        }

        List<string> allVersions = await _db.GetAllVersionsAsync(decodedName);
        var versionsDict = new Dictionary<string, VersionDetailResponse>();
        foreach (string v in allVersions)
        {
            VersionRecord? vr = await _db.GetPackageVersionAsync(decodedName, v);
            if (vr != null)
            {
                versionsDict[v] = BuildVersionResponse(vr);
            }
        }

        List<PackageRoleEntry> roles = await _db.GetPackageRolesAsync(decodedName);
        List<string> owners = roles
            .Where(r => r.PrincipalType == "user" && r.Role == "owner")
            .Select(r => r.PrincipalId)
            .OrderBy(u => u)
            .ToList();
        List<string>? keywords = null;
        if (package.Keywords != null)
        {
            keywords = JsonSerializer.Deserialize<List<string>>(package.Keywords);
        }

        var response = new PackageDetailResponse
        {
            Name = package.Name,
            Description = package.Description,
            Owners = owners,
            License = package.License,
            Repository = package.Repository,
            Keywords = keywords ?? new List<string>(),
            Readme = package.Readme,
            Versions = versionsDict,
            Latest = package.Latest,
            CreatedAt = package.CreatedAt.ToString("o"),
            UpdatedAt = package.UpdatedAt.ToString("o"),
            Deprecated = package.Deprecated,
            DeprecationMessage = package.DeprecationMessage,
            DeprecationAlternative = package.DeprecationAlternative
        };

        return Ok(response);
    }

    /// <summary>
    /// Gets metadata for a specific version of a package.
    /// </summary>
    /// <param name="name">The URL-encoded package name.</param>
    /// <param name="version">The exact semantic version string.</param>
    /// <remarks>No authentication required.</remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="VersionDetailResponse"/>,
    /// or <c>404</c> if the package or version does not exist.
    /// </returns>
    [AllowAnonymous]
    [HttpGet("{name}/{version}")]
    public async Task<IActionResult> GetVersion(string name, string version)
    {
        string decodedName = Uri.UnescapeDataString(name);

        PackageRecord? package = await _db.GetPackageAsync(decodedName);
        if (package == null || !await CanReadPackageAsync(package))
        {
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{decodedName}' not found." });
        }

        VersionRecord? vr = await _db.GetPackageVersionAsync(decodedName, version);
        if (vr == null)
        {
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{decodedName}' not found." });
        }

        return Ok(BuildVersionResponse(vr));
    }

    /// <summary>
    /// Downloads the tarball for a specific package version.
    /// </summary>
    /// <param name="name">The URL-encoded package name.</param>
    /// <param name="version">The exact semantic version string.</param>
    /// <remarks>
    /// Streams the compressed tarball from <see cref="IPackageStorage"/> with content
    /// type <c>application/gzip</c>. No authentication required.
    /// When the version record has a non-empty <c>Integrity</c> field the response
    /// includes an <c>X-Integrity</c> header (format <c>sha256-&lt;base64&gt;</c>)
    /// that clients can use to verify the downloaded bytes.
    /// </remarks>
    /// <returns>
    /// <c>200</c> file stream on success,
    /// or <c>404</c> if the version record or its tarball cannot be found.
    /// </returns>
    [AllowAnonymous]
    [HttpGet("{name}/{version}/download")]
    public async Task<IActionResult> DownloadVersion(string name, string version)
    {
        string decodedName = Uri.UnescapeDataString(name);

        PackageRecord? package = await _db.GetPackageAsync(decodedName);
        if (package == null || !await CanReadPackageAsync(package))
        {
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{decodedName}' not found." });
        }

        VersionRecord? versionRecord = await _db.GetPackageVersionAsync(decodedName, version);
        if (versionRecord == null)
        {
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{decodedName}' not found." });
        }

        Stream? stream = await _storage.RetrieveAsync(decodedName, version);
        if (stream == null)
        {
            return NotFound(new ErrorResponse { Error = "Package tarball not found in storage." });
        }

        if (!string.IsNullOrEmpty(versionRecord.Integrity))
        {
            Response.Headers["X-Integrity"] = versionRecord.Integrity;
        }

        return File(stream, "application/gzip", $"{decodedName.Replace('/', '-')}-{version}.tgz");
    }

    /// <summary>
    /// Publishes a new package or a new version of an existing package.
    /// </summary>
    /// <param name="name">The URL-encoded package name to publish under.</param>
    /// <remarks>
    /// Reads the package tarball from the raw request body. An optional
    /// <c>X-Integrity</c> header may be supplied for client-side integrity
    /// verification. The caller must be an owner of an existing package, or the
    /// package must not yet exist. Requires a JWT with the <c>publish</c> or
    /// <c>admin</c> scope.
    /// </remarks>
    /// <returns>
    /// <c>201</c> with a <see cref="PublishResponse"/> containing the package name,
    /// version, and integrity hash,
    /// <c>400</c> if the tarball is malformed (missing manifest, no <c>.stash</c> files,
    /// integrity mismatch, etc.),
    /// <c>401</c> if unauthenticated,
    /// <c>403</c> if the user is not an owner of the package,
    /// or <c>409</c> with a <see cref="VersionConflictResponse"/> if the version already exists.
    /// </returns>
    [Authorize(Policy = "RequirePublishScope")]
    [HttpPut("{name}")]
    public async Task<IActionResult> PublishPackage(string name)
    {
        string decodedName = Uri.UnescapeDataString(name);
        string username = User.Identity!.Name!;

        bool packageExists = await _db.PackageExistsAsync(decodedName);
        bool isOwner = packageExists && await _db.HasPackagePermissionAsync(decodedName, username, "owner");
        if (packageExists && !isOwner)
        {
            return StatusCode(403, new ErrorResponse { Error = $"User '{username}' is not an owner of '{decodedName}'." });
        }

        string? clientIntegrity = Request.Headers["X-Integrity"];

        try
        {
            VersionRecord vr = await _packageService.Publish(Request.Body, username, clientIntegrity);
            string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _auditService.LogPublishAsync(vr.PackageName, vr.Version, username, ip);
            return StatusCode(201, new PublishResponse { Package = vr.PackageName, Version = vr.Version, Integrity = vr.Integrity });
        }
        catch (VersionConflictException ex)
        {
            return Conflict(new VersionConflictResponse { Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Removes a specific version of a package from the registry.
    /// </summary>
    /// <param name="name">The URL-encoded package name.</param>
    /// <param name="version">The exact semantic version string to remove.</param>
    /// <remarks>
    /// The caller must be an owner of the package or hold an <c>admin</c>-scoped
    /// token. Both the metadata record and the stored tarball are removed. The
    /// operation is recorded in the audit log. Requires a JWT with the
    /// <c>publish</c> or <c>admin</c> scope.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with an <see cref="UnpublishResponse"/> on success,
    /// <c>400</c> if the version does not exist,
    /// <c>401</c> if unauthenticated,
    /// or <c>403</c> if the caller does not have permission.
    /// </returns>
    [Authorize(Policy = "RequirePublishScope")]
    [HttpDelete("{name}/{version}")]
    public async Task<IActionResult> UnpublishVersion(string name, string version)
    {
        string decodedName = Uri.UnescapeDataString(name);
        string username = User.Identity!.Name!;

        try
        {
            await _packageService.UnpublishAsync(decodedName, version, username);
            string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _auditService.LogUnpublishAsync(decodedName, version, username, ip);
            return Ok(new UnpublishResponse { Package = decodedName, Version = version });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new ErrorResponse { Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    // ── Deprecation endpoints ─────────────────────────────────────────────────

    /// <summary>
    /// Marks an entire package as deprecated.
    /// </summary>
    /// <param name="name">The URL-encoded package name.</param>
    /// <returns>
    /// <c>200</c> with a <see cref="DeprecationResponse"/> on success,
    /// <c>400</c> if the request body is invalid or the package does not exist,
    /// or <c>403</c> if the caller is not an owner.
    /// </returns>
    [Authorize(Policy = "RequirePublishScope")]
    [HttpPatch("{name}/deprecate")]
    public async Task<IActionResult> DeprecatePackage(string name, [FromBody] DeprecatePackageRequest request)
    {
        string decodedName = Uri.UnescapeDataString(name);
        string username = User.Identity!.Name!;

        if (!await _db.PackageExistsAsync(decodedName))
        {
            return NotFound(new ErrorResponse { Error = $"Package '{decodedName}' not found." });
        }

        bool isOwner = await _db.HasPackagePermissionAsync(decodedName, username, "owner");
        bool isAdmin = User.IsInRole("admin");
        if (!isOwner && !isAdmin)
        {
            return StatusCode(403, new ErrorResponse { Error = $"User '{username}' is not an owner of '{decodedName}'." });
        }

        try
        {
            await _deprecationService.DeprecatePackageAsync(decodedName, request.Message, request.Alternative, username);
            string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _auditService.LogPackageDeprecateAsync(decodedName, username, ip);
            return Ok(new DeprecationResponse { Package = decodedName, Deprecated = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Removes the deprecation status from a package.
    /// </summary>
    /// <param name="name">The URL-encoded package name.</param>
    /// <returns>
    /// <c>200</c> with a <see cref="DeprecationResponse"/> on success,
    /// <c>400</c> if the package does not exist,
    /// or <c>403</c> if the caller is not an owner.
    /// </returns>
    [Authorize(Policy = "RequirePublishScope")]
    [HttpDelete("{name}/deprecate")]
    public async Task<IActionResult> UndeprecatePackage(string name)
    {
        string decodedName = Uri.UnescapeDataString(name);
        string username = User.Identity!.Name!;

        if (!await _db.PackageExistsAsync(decodedName))
        {
            return NotFound(new ErrorResponse { Error = $"Package '{decodedName}' not found." });
        }

        bool isOwner = await _db.HasPackagePermissionAsync(decodedName, username, "owner");
        bool isAdmin = User.IsInRole("admin");
        if (!isOwner && !isAdmin)
        {
            return StatusCode(403, new ErrorResponse { Error = $"User '{username}' is not an owner of '{decodedName}'." });
        }

        try
        {
            await _deprecationService.UndeprecatePackageAsync(decodedName);
            string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _auditService.LogPackageUndeprecateAsync(decodedName, username, ip);
            return Ok(new DeprecationResponse { Package = decodedName, Deprecated = false });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Marks a specific version of a package as deprecated.
    /// </summary>
    /// <param name="name">The URL-encoded package name.</param>
    /// <param name="version">The exact semantic version string.</param>
    /// <returns>
    /// <c>200</c> with a <see cref="DeprecationResponse"/> on success,
    /// <c>400</c> if the package or version does not exist,
    /// or <c>403</c> if the caller is not an owner.
    /// </returns>
    [Authorize(Policy = "RequirePublishScope")]
    [HttpPatch("{name}/{version}/deprecate")]
    public async Task<IActionResult> DeprecateVersion(string name, string version, [FromBody] DeprecateVersionRequest request)
    {
        string decodedName = Uri.UnescapeDataString(name);
        string username = User.Identity!.Name!;

        if (!await _db.VersionExistsAsync(decodedName, version))
        {
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{decodedName}' not found." });
        }

        bool isOwner = await _db.HasPackagePermissionAsync(decodedName, username, "owner");
        bool isAdmin = User.IsInRole("admin");
        if (!isOwner && !isAdmin)
        {
            return StatusCode(403, new ErrorResponse { Error = $"User '{username}' is not an owner of '{decodedName}'." });
        }

        try
        {
            await _deprecationService.DeprecateVersionAsync(decodedName, version, request.Message, username);
            string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _auditService.LogVersionDeprecateAsync(decodedName, version, username, ip);
            return Ok(new DeprecationResponse { Package = decodedName, Version = version, Deprecated = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Removes the deprecation status from a specific version.
    /// </summary>
    /// <param name="name">The URL-encoded package name.</param>
    /// <param name="version">The exact semantic version string.</param>
    /// <returns>
    /// <c>200</c> with a <see cref="DeprecationResponse"/> on success,
    /// <c>400</c> if the package or version does not exist,
    /// or <c>403</c> if the caller is not an owner.
    /// </returns>
    [Authorize(Policy = "RequirePublishScope")]
    [HttpDelete("{name}/{version}/deprecate")]
    public async Task<IActionResult> UndeprecateVersion(string name, string version)
    {
        string decodedName = Uri.UnescapeDataString(name);
        string username = User.Identity!.Name!;

        if (!await _db.VersionExistsAsync(decodedName, version))
        {
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{decodedName}' not found." });
        }

        bool isOwner = await _db.HasPackagePermissionAsync(decodedName, username, "owner");
        bool isAdmin = User.IsInRole("admin");
        if (!isOwner && !isAdmin)
        {
            return StatusCode(403, new ErrorResponse { Error = $"User '{username}' is not an owner of '{decodedName}'." });
        }

        try
        {
            await _deprecationService.UndeprecateVersionAsync(decodedName, version);
            string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _auditService.LogVersionUndeprecateAsync(decodedName, version, username, ip);
            return Ok(new DeprecationResponse { Package = decodedName, Version = version, Deprecated = false });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    // ── Visibility endpoint ───────────────────────────────────────────────────

    /// <summary>
    /// Changes the visibility of a package. Only the package owner may invoke this endpoint.
    /// </summary>
    /// <param name="name">The URL-encoded package name.</param>
    /// <returns>
    /// <c>200</c> with a <see cref="SetVisibilityResponse"/> on success,
    /// <c>400</c> if the visibility value is invalid or the package does not exist,
    /// <c>401</c> if unauthenticated,
    /// or <c>403</c> if the caller is not an owner.
    /// </returns>
    [Authorize(Policy = "RequirePublishScope")]
    [HttpPatch("{name}/visibility")]
    public async Task<IActionResult> SetVisibility(string name, [FromBody] SetVisibilityRequest request)
    {
        string decodedName = Uri.UnescapeDataString(name);
        string username = User.Identity!.Name!;

        if (!await _db.PackageExistsAsync(decodedName))
        {
            return NotFound(new ErrorResponse { Error = $"Package '{decodedName}' not found." });
        }

        bool isOwner = await _db.HasPackagePermissionAsync(decodedName, username, "owner");
        bool isAdmin = User.IsInRole("admin");
        if (!isOwner && !isAdmin)
        {
            return StatusCode(403, new ErrorResponse { Error = $"User '{username}' is not an owner of '{decodedName}'." });
        }

        string[] validVisibilities = ["public", "private", "internal"];
        if (string.IsNullOrEmpty(request.Visibility) || !Array.Exists(validVisibilities, v => v == request.Visibility))
        {
            return BadRequest(new ErrorResponse { Error = $"Invalid visibility value '{request.Visibility}'. Must be one of: public, private, internal." });
        }

        await _db.SetPackageVisibilityAsync(decodedName, request.Visibility);

        return Ok(new SetVisibilityResponse { Package = decodedName, Visibility = request.Visibility });
    }

    // ── Visibility helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when the current caller is allowed to read the package.
    /// <list type="bullet">
    ///   <item><description><c>public</c> — always readable.</description></item>
    ///   <item><description><c>private</c> — caller must have a read-scoped JWT AND at least <c>reader</c> permission.</description></item>
    ///   <item><description><c>internal</c> — same requirements as <c>private</c> in P4; org-member shortcut is added in P5.</description></item>
    /// </list>
    /// </summary>
    private async Task<bool> CanReadPackageAsync(PackageRecord package)
    {
        if (package.Visibility == "public")
            return true;

        // private / internal: require authenticated caller with read-scoped JWT
        if (!User.Identity?.IsAuthenticated ?? true)
            return false;

        // Validate token scope (read, publish, or admin)
        string? tokenScope = User.FindFirst("token_scope")?.Value;
        if (tokenScope is not ("read" or "publish" or "admin"))
            return false;

        // Check the caller has at least reader permission on the package
        string username = User.Identity!.Name!;
        bool isAdmin = User.IsInRole("admin");
        if (isAdmin)
            return true;

        return await _db.HasPackagePermissionAsync(package.Name, username, "reader");
    }

    /// <summary>
    /// Maps a <see cref="VersionRecord"/> entity to its API response shape.
    /// </summary>
    /// <param name="vr">The version record to convert.</param>
    /// <returns>A <see cref="VersionDetailResponse"/> populated from the record's fields.</returns>
    private static VersionDetailResponse BuildVersionResponse(VersionRecord vr)
    {
        Dictionary<string, object>? deps = null;
        if (vr.Dependencies != null)
        {
            deps = JsonSerializer.Deserialize<Dictionary<string, object>>(vr.Dependencies);
        }

        return new VersionDetailResponse
        {
            Version = vr.Version,
            StashVersion = vr.StashVersion,
            Dependencies = deps ?? new Dictionary<string, object>(),
            Integrity = vr.Integrity,
            PublishedAt = vr.PublishedAt.ToString("o"),
            PublishedBy = vr.PublishedBy,
            Deprecated = vr.Deprecated,
            DeprecationMessage = vr.DeprecationMessage
        };
    }
}
