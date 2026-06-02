using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Auth;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;

namespace Stash.Registry.Controllers;

/// <summary>
/// REST API controller for administrative operations.
/// </summary>
/// <remarks>
/// <para>
/// All endpoints in this controller require a JWT with the <c>admin</c> role
/// and an admin token ceiling, enforced per-endpoint by
/// <see cref="IRegistryAuthorizer"/> with the admin-ceiling-first check. The
/// class-level <c>[Authorize]</c> only requires authentication; the PDP makes
/// the authorization decision.
/// </para>
/// </remarks>
[Authorize]
[ApiController]
[Route("api/v1/admin")]
public class AdminController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IAuthProvider _authProvider;
    private readonly AuditService _auditService;
    private readonly PackageRoleService _roleService;
    private readonly RegistryConfig _config;

    /// <summary>
    /// Initialises the controller with its required services.
    /// </summary>
    public AdminController(
        IRegistryDatabase db,
        IAuthProvider authProvider,
        AuditService auditService,
        PackageRoleService roleService,
        RegistryConfig config)
    {
        _db = db;
        _authProvider = authProvider;
        _auditService = auditService;
        _roleService = roleService;
        _config = config;
    }

    /// <summary>
    /// Returns high-level registry statistics.
    /// </summary>
    [RegistryAuthorize(RegistryAction.ReadAdminStats)]
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        int users = (await _db.ListUsersAsync()).Count;
        return Ok(new StatsResponse { Users = users });
    }

    /// <summary>
    /// Creates a new user account as an administrator.
    /// </summary>
    [RegistryAuthorize(RegistryAction.ManageUser)]
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser()
    {
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        CreateUserRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<CreateUserRequest>(Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? newUsername = body?.Username?.Trim();
        string? password = body?.Password;
        string newRole = string.IsNullOrEmpty(body?.Role) ? UserRoles.User : body.Role;

        if (string.IsNullOrEmpty(newUsername) || string.IsNullOrEmpty(password))
            return BadRequest(new ErrorResponse { Error = "Username and password are required." });

        if (newUsername.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(newUsername, @"^[a-zA-Z0-9_-]+$"))
            return BadRequest(new ErrorResponse { Error = "Username must be 1-64 characters and contain only letters, digits, hyphens, or underscores." });

        if (password.Length < 8)
            return BadRequest(new ErrorResponse { Error = "Password must be at least 8 characters." });

        try
        {
            await _authProvider.CreateUserAsync(newUsername, password);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }

        if (string.Equals(newRole, UserRoles.Admin, StringComparison.Ordinal))
            await _db.UpdateUserRoleAsync(newUsername, UserRoles.Admin);

        await _auditService.LogUserCreateAsync(newUsername, ip);

        return StatusCode(201, new CreateUserResponse { Username = newUsername, Role = newRole });
    }

    /// <summary>
    /// Deletes a user account and its associated data.
    /// </summary>
    [RegistryAuthorize(RegistryAction.ManageUser)]
    [HttpDelete("users/{username}")]
    public async Task<IActionResult> DeleteUser(string username)
    {
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        string decodedUsername = Uri.UnescapeDataString(username);
        UserRecord? user = await _db.GetUserAsync(decodedUsername);
        if (user == null)
            return NotFound(new ErrorResponse { Error = $"User '{decodedUsername}' not found." });

        await _db.DeleteUserAsync(decodedUsername);

        string actingUser = User.Identity!.Name!;
        await _auditService.LogUserDisableAsync(actingUser, decodedUsername, ip);

        return Ok(new SuccessResponse());
    }

    /// <summary>
    /// Assigns a role to a principal on a package (admin override).
    /// </summary>
    [RegistryAuthorize(RegistryAction.AdminAssignPackageRole)]
    [HttpPut("packages/{scope}/{name}/roles")]
    public async Task<IActionResult> AdminAssignRole(string scope, string name)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;
        string username = User.Identity!.Name!;

        if (!await _db.PackageExistsAsync(packageName))
            return NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        AssignRoleRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<AssignRoleRequest>(
                Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        if (body == null)
            return BadRequest(new ErrorResponse { Error = "Request body is required." });

        if (!Array.Exists(PrincipalTypes.All, p => p == body.PrincipalType))
            return BadRequest(new ErrorResponse { Error = $"Invalid principal_type '{body.PrincipalType}'. Must be one of: {string.Join(", ", PrincipalTypes.All)}." });

        if (!Array.Exists(PackageRoles.RankOrder, r => r == body.Role))
            return BadRequest(new ErrorResponse { Error = $"Invalid role '{body.Role}'. Must be one of: owner, maintainer, publisher, reader." });

        await _db.AssignPackageRoleAsync(packageName, body.PrincipalType, body.PrincipalId, body.Role);
        await _auditService.LogRoleMutationAllowAsync("role.assign", username, packageName, body.PrincipalId, ip);

        return Ok(new SuccessResponse());
    }

    /// <summary>
    /// Revokes the role of a principal on a package (admin override).
    /// </summary>
    /// <remarks>
    /// Returns 204 on success, 404 for missing package or missing role,
    /// 409 if the revoke would drop the owner count to zero.
    /// Unlike the owner-gated endpoint this uses <c>AdminRevokePackageRole</c> action
    /// so the PDP applies the admin short-circuit.
    /// </remarks>
    [RegistryAuthorize(RegistryAction.AdminRevokePackageRole)]
    [HttpDelete("packages/{scope}/{name}/roles")]
    public async Task<IActionResult> AdminRevokeRole(string scope, string name, [FromBody] RevokeRoleRequest request)
    {
        var resource = PackageRoute.From(Uri.UnescapeDataString(scope), Uri.UnescapeDataString(name));
        string packageName = resource.FullName;

        if (!await _db.PackageExistsAsync(packageName))
            return NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        string username = User.Identity!.Name!;

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

    /// <summary>
    /// Returns a paginated list of audit log entries.
    /// </summary>
    [RegistryAuthorize(RegistryAction.ReadAuditLog)]
    [HttpGet("audit-log")]
    public async Task<IActionResult> GetAuditLog()
    {
        int page = 1;
        int pageSize = 50;
        string? packageFilter = null;
        string? actionFilter = null;

        if (Request.Query.TryGetValue("page", out var pageStr) && int.TryParse(pageStr, out int parsedPage) && parsedPage > 0)
            page = parsedPage;

        if (Request.Query.TryGetValue("pageSize", out var pageSizeStr) && int.TryParse(pageSizeStr, out int parsedPageSize) && parsedPageSize > 0)
            pageSize = Math.Min(parsedPageSize, 200);

        if (Request.Query.TryGetValue("package", out var pkgFilter) && !string.IsNullOrEmpty(pkgFilter))
            packageFilter = pkgFilter.ToString();

        if (Request.Query.TryGetValue("action", out var actFilter) && !string.IsNullOrEmpty(actFilter))
            actionFilter = actFilter.ToString();

        var result = await _auditService.GetAuditLogAsync(page, pageSize, packageFilter, actionFilter);
        int totalPages = (int)Math.Ceiling(result.TotalCount / (double)pageSize);

        return Ok(new AuditLogResponse
        {
            Entries = result.Items.Select(e => new AuditEntryResponse
            {
                Action = e.Action,
                Package = e.Package,
                Version = e.Version,
                User = e.User,
                Target = e.Target,
                Ip = e.Ip,
                Timestamp = e.Timestamp,
                Decision = e.Decision,
                DenyReason = e.DenyReason
            }).ToList(),
            TotalCount = result.TotalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        });
    }
}
