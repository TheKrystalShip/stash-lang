using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stash.Common;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using Stash.Registry.Storage;

namespace Stash.Registry.Controllers;

/// <summary>
/// REST API controller for package operations (get, publish, unpublish, download, roles, visibility).
/// </summary>
/// <remarks>
/// <para>
/// All authorization decisions are delegated to <see cref="IRegistryAuthorizer"/> (the PDP).
/// No hard-coded role or permission strings remain in this controller (P3 migration).
/// </para>
/// <para>
/// For private packages, the PDP returns <see cref="AuthzDenyReason.VisibilityHidden"/>;
/// the controller maps this to <c>404</c> (not <c>403</c>) to avoid leaking the
/// package's existence to unauthorized callers.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/packages")]
public class PackagesController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IPackageStorage _storage;
    private readonly PackageService _packageService;
    private readonly PackageRoleService _roleService;
    private readonly AuditService _auditService;
    private readonly DeprecationService _deprecationService;
    private readonly IRegistryAuthorizer _authorizer;
    private readonly RegistryConfig _config;

    /// <summary>
    /// Initialises the controller with its required services.
    /// </summary>
    public PackagesController(
        IRegistryDatabase db,
        IPackageStorage storage,
        PackageService packageService,
        PackageRoleService roleService,
        AuditService auditService,
        DeprecationService deprecationService,
        IRegistryAuthorizer authorizer,
        RegistryConfig config)
    {
        _db = db;
        _storage = storage;
        _packageService = packageService;
        _roleService = roleService;
        _auditService = auditService;
        _deprecationService = deprecationService;
        _authorizer = authorizer;
        _config = config;
    }

    // ── Helper: build Principal from the current HttpContext ─────────────────

    private Principal BuildPrincipal()
    {
        if (User?.Identity?.IsAuthenticated != true)
            return new AnonymousPrincipal();

        string username = User.Identity!.Name!;
        bool isAdmin = User.IsInRole("admin");
        string tokenScopeStr = User.FindFirstValue("token_scope") ?? "read";
        TokenCeiling ceiling = tokenScopeStr switch
        {
            "admin" => TokenCeiling.Admin,
            "publish" => TokenCeiling.Publish,
            _ => TokenCeiling.Read
        };
        UserRole role = isAdmin ? UserRole.Admin : UserRole.User;
        string tokenId = User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti) ?? "";
        return new UserPrincipal(username, role, ceiling, tokenId);
    }

    /// <summary>
    /// Maps an <see cref="AuthzDenyReason"/> to an HTTP status code.
    /// <see cref="AuthzDenyReason.VisibilityHidden"/> and
    /// <see cref="AuthzDenyReason.PackageNotFound"/> map to 404;
    /// <see cref="AuthzDenyReason.NotAuthenticated"/> maps to 401;
    /// all others map to 403.
    /// </summary>
    private static int DenyReasonToStatus(AuthzDenyReason reason) => reason switch
    {
        AuthzDenyReason.VisibilityHidden => 404,
        AuthzDenyReason.PackageNotFound => 404,
        AuthzDenyReason.NotAuthenticated => 401,
        _ => 403
    };

    // ── Read endpoints ────────────────────────────────────────────────────────

    /// <summary>
    /// Gets package metadata including all published versions.
    /// </summary>
    [PublicEndpoint("package metadata is public for public packages; visibility enforced by IRegistryAuthorizer")]
    [HttpGet("{scope}/{name}")]
    public async Task<IActionResult> GetPackage(string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        PackageRecord? package = await _db.GetPackageAsync(packageName);
        if (package == null)
            return NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        var principal = BuildPrincipal();
        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.ReadPackageMetadata, resource);
        if (!decision.Allowed)
        {
            // Anonymous denial on private packages: 404 (do not leak existence)
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = $"Package '{packageName}' not found." });
        }

        List<string> allVersions = await _db.GetAllVersionsAsync(packageName);
        var versionsDict = new Dictionary<string, VersionDetailResponse>();
        foreach (string v in allVersions)
        {
            VersionRecord? vr = await _db.GetPackageVersionAsync(packageName, v);
            if (vr != null)
                versionsDict[v] = BuildVersionResponse(vr);
        }

        List<PackageRoleEntry> roles = await _db.GetPackageRolesAsync(packageName);
        List<string> owners = roles
            .Where(r => r.PrincipalType == "user" && r.Role == "owner")
            .Select(r => r.PrincipalId)
            .OrderBy(u => u)
            .ToList();

        List<string>? keywords = package.Keywords != null
            ? JsonSerializer.Deserialize<List<string>>(package.Keywords)
            : null;

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
    [PublicEndpoint("version metadata is public for public packages; visibility gated by IRegistryAuthorizer")]
    [HttpGet("{scope}/{name}/{version}")]
    public async Task<IActionResult> GetVersion(string scope, string name, string version)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        PackageRecord? package = await _db.GetPackageAsync(packageName);
        if (package == null)
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        var principal = BuildPrincipal();
        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.ReadPackageVersion, resource);
        if (!decision.Allowed)
        {
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });
        }

        VersionRecord? vr = await _db.GetPackageVersionAsync(packageName, version);
        if (vr == null)
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        return Ok(BuildVersionResponse(vr));
    }

    /// <summary>
    /// Downloads the tarball for a specific package version.
    /// </summary>
    [PublicEndpoint("tarball download is public for public packages; visibility gated by IRegistryAuthorizer")]
    [HttpGet("{scope}/{name}/{version}/download")]
    public async Task<IActionResult> DownloadVersion(string scope, string name, string version)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        PackageRecord? package = await _db.GetPackageAsync(packageName);
        if (package == null)
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        var principal = BuildPrincipal();
        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.DownloadPackageVersion, resource);
        if (!decision.Allowed)
        {
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });
        }

        VersionRecord? versionRecord = await _db.GetPackageVersionAsync(packageName, version);
        if (versionRecord == null)
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        Stream? stream = await _storage.RetrieveAsync(packageName, version);
        if (stream == null)
            return NotFound(new ErrorResponse { Error = "Package tarball not found in storage." });

        if (!string.IsNullOrEmpty(versionRecord.Integrity))
            Response.Headers["X-Integrity"] = versionRecord.Integrity;

        return File(stream, "application/gzip", $"{packageName.Replace('/', '-').TrimStart('@', '-')}-{version}.tgz");
    }

    // ── Write endpoints ───────────────────────────────────────────────────────

    /// <summary>
    /// Publishes a new package or a new version of an existing package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses <see cref="RegistryAction.CreatePackage"/> when the package does not yet exist
    /// and <see cref="RegistryAction.PublishVersion"/> when it does.  Both decisions are
    /// made by the PDP before the tarball is parsed.
    /// </para>
    /// <para>
    /// The optional <c>X-Package-Version</c> header allows callers to assert the
    /// manifest version they intend to publish.  A disagreement with the manifest's
    /// <c>version</c> field returns 400 ManifestRouteMismatch (Q5 closure).
    /// </para>
    /// </remarks>
    [Authorize]
    [HttpPut("{scope}/{name}")]
    public async Task<IActionResult> PublishPackage(string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        var principal = BuildPrincipal();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        // Determine action: CreatePackage or PublishVersion
        bool packageExists = await _db.PackageExistsAsync(packageName);
        var action = packageExists ? RegistryAction.PublishVersion : RegistryAction.CreatePackage;

        var decision = await _authorizer.AuthorizeAsync(principal, action, resource);
        if (!decision.Allowed)
        {
            // Audit authenticated denials only
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync(action.ToString(), up.Username, packageName, decision.Reason, ip);

            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse
                {
                    Error = decision.Reason.ToString(),
                    Message = decision.Detail
                });
        }

        string? clientIntegrity = Request.Headers["X-Integrity"];
        string? expectedVersion = Request.Headers["X-Package-Version"].Count > 0
            ? Request.Headers["X-Package-Version"].ToString()
            : null;
        string username = ((UserPrincipal)principal).Username;

        try
        {
            var (vr, isNewPackage) = await _packageService.PublishAsync(
                resource, Request.Body, principal, clientIntegrity, expectedVersion);

            // Audit: emit one allow entry per successful mutation
            string auditAction = isNewPackage ? "package.create" : "package.publish";
            await _auditService.LogMutationAllowAsync(auditAction, username, packageName, ip);

            return StatusCode(201, new PublishResponse
            {
                Package = vr.PackageName,
                Version = vr.Version,
                Integrity = vr.Integrity
            });
        }
        catch (ManifestRouteMismatchException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ManifestRouteMismatch",
                Message = ex.Message
            });
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
    [Authorize]
    [HttpDelete("{scope}/{name}/{version}")]
    public async Task<IActionResult> UnpublishVersion(string scope, string name, string version)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        var principal = BuildPrincipal();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.UnpublishVersion, resource);
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("UnpublishVersion", up.Username, packageName, decision.Reason, ip);
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        string username = ((UserPrincipal)principal).Username;
        try
        {
            await _packageService.UnpublishAsync(packageName, version, username);
            await _auditService.LogMutationAllowAsync("package.unpublish", username, packageName, ip);
            return Ok(new UnpublishResponse { Package = packageName, Version = version });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    // ── Deprecation endpoints ─────────────────────────────────────────────────────

    /// <summary>
    /// Marks an entire package as deprecated.
    /// </summary>
    [Authorize]
    [HttpPatch("{scope}/{name}/deprecate")]
    public async Task<IActionResult> DeprecatePackage(string scope, string name, [FromBody] DeprecatePackageRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        var principal = BuildPrincipal();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!await _db.PackageExistsAsync(packageName))
            return NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.DeprecatePackage, resource);
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("DeprecatePackage", up.Username, packageName, decision.Reason, ip);
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        string username = ((UserPrincipal)principal).Username;
        try
        {
            await _deprecationService.DeprecatePackageAsync(packageName, request.Message, request.Alternative, username);
            await _auditService.LogMutationAllowAsync("package.deprecate", username, packageName, ip);
            return Ok(new DeprecationResponse { Package = packageName, Deprecated = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Removes the deprecation status from a package.
    /// </summary>
    [Authorize]
    [HttpDelete("{scope}/{name}/deprecate")]
    public async Task<IActionResult> UndeprecatePackage(string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        var principal = BuildPrincipal();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!await _db.PackageExistsAsync(packageName))
            return NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.DeprecatePackage, resource);
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("UndeprecatePackage", up.Username, packageName, decision.Reason, ip);
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        string username = ((UserPrincipal)principal).Username;
        try
        {
            await _deprecationService.UndeprecatePackageAsync(packageName);
            await _auditService.LogMutationAllowAsync("package.undeprecate", username, packageName, ip);
            return Ok(new DeprecationResponse { Package = packageName, Deprecated = false });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Marks a specific version of a package as deprecated.
    /// </summary>
    [Authorize]
    [HttpPatch("{scope}/{name}/{version}/deprecate")]
    public async Task<IActionResult> DeprecateVersion(string scope, string name, string version, [FromBody] DeprecateVersionRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        var principal = BuildPrincipal();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!await _db.VersionExistsAsync(packageName, version))
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.DeprecateVersion, resource);
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("DeprecateVersion", up.Username, packageName, decision.Reason, ip);
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        string username = ((UserPrincipal)principal).Username;
        try
        {
            await _deprecationService.DeprecateVersionAsync(packageName, version, request.Message, username);
            await _auditService.LogMutationAllowAsync("version.deprecate", username, packageName, ip);
            return Ok(new DeprecationResponse { Package = packageName, Version = version, Deprecated = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    /// <summary>
    /// Removes the deprecation status from a specific version.
    /// </summary>
    [Authorize]
    [HttpDelete("{scope}/{name}/{version}/deprecate")]
    public async Task<IActionResult> UndeprecateVersion(string scope, string name, string version)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        var principal = BuildPrincipal();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!await _db.VersionExistsAsync(packageName, version))
            return NotFound(new ErrorResponse { Error = $"Version '{version}' of package '{packageName}' not found." });

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.DeprecateVersion, resource);
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("UndeprecateVersion", up.Username, packageName, decision.Reason, ip);
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        string username = ((UserPrincipal)principal).Username;
        try
        {
            await _deprecationService.UndeprecateVersionAsync(packageName, version);
            await _auditService.LogMutationAllowAsync("version.undeprecate", username, packageName, ip);
            return Ok(new DeprecationResponse { Package = packageName, Version = version, Deprecated = false });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    // ── Role endpoints ────────────────────────────────────────────────────────

    /// <summary>
    /// Lists all role entries for a package.
    /// </summary>
    [Authorize]
    [HttpGet("{scope}/{name}/roles")]
    public async Task<IActionResult> GetRoles(string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        if (!await _db.PackageExistsAsync(packageName))
            return NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        var principal = BuildPrincipal();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.ListPackageRoles, resource);
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("ListPackageRoles", up.Username, packageName, decision.Reason, ip);
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        List<PackageRoleEntry> roles = await _db.GetPackageRolesAsync(packageName);
        return Ok(new PackageRolesListResponse
        {
            Package = packageName,
            Roles = roles.Select(r => new PackageRoleResponse
            {
                PrincipalType = r.PrincipalType,
                PrincipalId = r.PrincipalId,
                Role = r.Role
            }).ToList()
        });
    }

    /// <summary>
    /// Assigns a role to a principal on a package.
    /// </summary>
    [Authorize]
    [HttpPut("{scope}/{name}/roles")]
    public async Task<IActionResult> AssignRole(string scope, string name, [FromBody] AssignRoleRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        if (!await _db.PackageExistsAsync(packageName))
            return NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        var principal = BuildPrincipal();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.AssignPackageRole, resource);
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("AssignPackageRole", up.Username, packageName, decision.Reason, ip);
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        string[] validPrincipalTypes = ["user", "team", "org"];
        if (!Array.Exists(validPrincipalTypes, p => p == request.PrincipalType))
            return BadRequest(new ErrorResponse { Error = $"Invalid principal_type '{request.PrincipalType}'. Must be one of: user, team, org." });

        string[] validRoles = ["owner", "maintainer", "publisher", "reader"];
        if (!Array.Exists(validRoles, r => r == request.Role))
            return BadRequest(new ErrorResponse { Error = $"Invalid role '{request.Role}'. Must be one of: owner, maintainer, publisher, reader." });

        string username = ((UserPrincipal)principal).Username;
        await _db.AssignPackageRoleAsync(packageName, request.PrincipalType, request.PrincipalId, request.Role);
        await _auditService.LogRoleMutationAllowAsync("role.assign", username, packageName, request.PrincipalId, ip);
        return Ok(new SuccessResponse());
    }

    /// <summary>
    /// Revokes the role of a principal on a package (owner self-service).
    /// </summary>
    /// <remarks>
    /// Returns 204 on success, 404 for missing package or missing role assignment,
    /// 409 if the revoke would drop the owner count to zero.
    /// </remarks>
    [Authorize]
    [HttpDelete("{scope}/{name}/roles")]
    public async Task<IActionResult> RevokeRole(string scope, string name, [FromBody] RevokeRoleRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        if (!await _db.PackageExistsAsync(packageName))
            return NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        var principal = BuildPrincipal();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.RevokePackageRole, resource);
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("RevokePackageRole", up.Username, packageName, decision.Reason, ip);
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        string username = ((UserPrincipal)principal).Username;
        try
        {
            await _roleService.RevokeRoleAsync(packageName, request.PrincipalType, request.PrincipalId);
            await _auditService.LogRoleMutationAllowAsync("role.revoke", username, packageName, request.PrincipalId, ip);
            return StatusCode(204);
        }
        catch (RoleNotFoundException)
        {
            return NotFound(new ErrorResponse { Error = $"Principal '{request.PrincipalType}:{request.PrincipalId}' holds no role on '{packageName}'." });
        }
        catch (LastOwnerException ex)
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }
    }

    // ── Visibility endpoint ───────────────────────────────────────────────────

    /// <summary>
    /// Changes the visibility of a package.
    /// </summary>
    [Authorize]
    [HttpPatch("{scope}/{name}/visibility")]
    public async Task<IActionResult> SetVisibility(string scope, string name, [FromBody] SetVisibilityRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        if (!await _db.PackageExistsAsync(packageName))
            return NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        var principal = BuildPrincipal();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var decision = await _authorizer.AuthorizeAsync(principal, RegistryAction.ChangePackageVisibility, resource);
        if (!decision.Allowed)
        {
            if (principal is UserPrincipal up)
                await _auditService.LogAuthzDenyAsync("ChangePackageVisibility", up.Username, packageName, decision.Reason, ip);
            return StatusCode(DenyReasonToStatus(decision.Reason),
                new ErrorResponse { Error = decision.Reason.ToString(), Message = decision.Detail });
        }

        string[] validVisibilities = ["public", "private", "internal"];
        if (string.IsNullOrEmpty(request.Visibility) || !Array.Exists(validVisibilities, v => v == request.Visibility))
        {
            return BadRequest(new ErrorResponse { Error = $"Invalid visibility value '{request.Visibility}'. Must be one of: public, private, internal." });
        }

        string username = ((UserPrincipal)principal).Username;
        await _db.SetPackageVisibilityAsync(packageName, request.Visibility);
        await _auditService.LogMutationAllowAsync("package.visibility_change", username, packageName, ip);
        return Ok(new SetVisibilityResponse { Package = packageName, Visibility = request.Visibility });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static VersionDetailResponse BuildVersionResponse(VersionRecord vr)
    {
        Dictionary<string, object>? deps = null;
        if (vr.Dependencies != null)
            deps = JsonSerializer.Deserialize<Dictionary<string, object>>(vr.Dependencies);

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
