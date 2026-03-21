using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
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

[ApiController]
[Route("api/v1/packages")]
public class PackagesController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IPackageStorage _storage;
    private readonly PackageService _packageService;
    private readonly AuditService _auditService;
    private readonly RegistryConfig _config;

    public PackagesController(
        IRegistryDatabase db,
        IPackageStorage storage,
        PackageService packageService,
        AuditService auditService,
        RegistryConfig config)
    {
        _db = db;
        _storage = storage;
        _packageService = packageService;
        _auditService = auditService;
        _config = config;
    }

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

        List<string> owners = await _db.GetOwnersAsync(decodedName);
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
            UpdatedAt = package.UpdatedAt.ToString("o")
        };

        return Ok(response);
    }

    [AllowAnonymous]
    [HttpGet("{name}/{version}")]
    public async Task<IActionResult> GetVersion(string name, string version)
    {
        string decodedName = Uri.UnescapeDataString(name);
        VersionRecord? vr = await _db.GetPackageVersionAsync(decodedName, version);
        if (vr == null)
        {
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{decodedName}' not found." });
        }

        return Ok(BuildVersionResponse(vr));
    }

    [AllowAnonymous]
    [HttpGet("{name}/{version}/download")]
    public async Task<IActionResult> DownloadVersion(string name, string version)
    {
        string decodedName = Uri.UnescapeDataString(name);
        if (!await _db.VersionExistsAsync(decodedName, version))
        {
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{decodedName}' not found." });
        }

        Stream? stream = await _storage.RetrieveAsync(decodedName, version);
        if (stream == null)
        {
            return NotFound(new ErrorResponse { Error = "Package tarball not found in storage." });
        }

        return File(stream, "application/gzip", $"{decodedName.Replace('/', '-')}-{version}.tgz");
    }

    [Authorize(Policy = "RequirePublishScope")]
    [HttpPut("{name}")]
    public async Task<IActionResult> PublishPackage(string name)
    {
        string decodedName = Uri.UnescapeDataString(name);
        string username = User.Identity!.Name!;

        bool packageExists = await _db.PackageExistsAsync(decodedName);
        bool isOwner = packageExists && await _db.IsOwnerAsync(decodedName, username);
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

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
            PublishedBy = vr.PublishedBy
        };
    }
}
