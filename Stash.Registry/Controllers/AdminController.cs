using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Auth;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using Stash.Registry.Contracts;

namespace Stash.Registry.Controllers;

/// <summary>
/// REST API controller for administrative operations.
/// </summary>
/// <remarks>
/// <para>
/// All endpoints in this controller require a JWT with the <c>admin</c> role,
/// enforced by the <c>RequireAdmin</c> authorization policy. Operations include
/// registry statistics, user management, package ownership adjustments, and
/// paginated audit log access.
/// </para>
/// </remarks>
[Authorize(Policy = "RequireAdmin")]
[ApiController]
[Route("api/v1/admin")]
public class AdminController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IAuthProvider _authProvider;
    private readonly AuditService _auditService;
    private readonly RegistryConfig _config;

    /// <summary>
    /// Initialises the controller with its required services.
    /// </summary>
    /// <param name="db">Registry database for user, package, and ownership queries.</param>
    /// <param name="authProvider">Provider used when creating new user accounts.</param>
    /// <param name="auditService">Service that records administrative audit events.</param>
    /// <param name="config">Registry-wide configuration.</param>
    public AdminController(
        IRegistryDatabase db,
        IAuthProvider authProvider,
        AuditService auditService,
        RegistryConfig config)
    {
        _db = db;
        _authProvider = authProvider;
        _auditService = auditService;
        _config = config;
    }

    /// <summary>
    /// Returns high-level registry statistics.
    /// </summary>
    /// <remarks>Requires an admin JWT.</remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="StatsResponse"/> containing the total registered
    /// user count.
    /// </returns>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        int users = (await _db.ListUsersAsync()).Count;
        return Ok(new StatsResponse { Users = users });
    }

    /// <summary>
    /// Creates a new user account as an administrator.
    /// </summary>
    /// <remarks>
    /// Reads a <see cref="CreateUserRequest"/> JSON body. If the <c>role</c> field is
    /// <c>admin</c>, the user's role is promoted immediately after creation via
    /// <see cref="IRegistryDatabase.UpdateUserRoleAsync"/>. The creation is recorded in
    /// the audit log. Requires an admin JWT.
    /// </remarks>
    /// <returns>
    /// <c>201</c> with a <see cref="CreateUserResponse"/> containing the new username and
    /// assigned role, <c>400</c> if validation fails, or <c>409</c> if the username is
    /// already taken.
    /// </returns>
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser()
    {
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
        string newRole = string.IsNullOrEmpty(body?.Role) ? "user" : body.Role;

        if (string.IsNullOrEmpty(newUsername) || string.IsNullOrEmpty(password))
        {
            return BadRequest(new ErrorResponse { Error = "Username and password are required." });
        }

        if (newUsername.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(newUsername, @"^[a-zA-Z0-9_-]+$"))
        {
            return BadRequest(new ErrorResponse { Error = "Username must be 1-64 characters and contain only letters, digits, hyphens, or underscores." });
        }

        if (password.Length < 8)
        {
            return BadRequest(new ErrorResponse { Error = "Password must be at least 8 characters." });
        }

        try
        {
            await _authProvider.CreateUserAsync(newUsername, password);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }

        if (string.Equals(newRole, "admin", StringComparison.Ordinal))
        {
            await _db.UpdateUserRoleAsync(newUsername, "admin");
        }

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogUserCreateAsync(newUsername, ip);

        return StatusCode(201, new CreateUserResponse { Username = newUsername, Role = newRole });
    }

    /// <summary>
    /// Deletes a user account and its associated data.
    /// </summary>
    /// <param name="username">The URL-encoded username to delete.</param>
    /// <remarks>
    /// Cascading behaviour: tokens are removed automatically via the foreign-key
    /// constraint; ownership entries are removed manually before the user row is
    /// deleted. The action is recorded in the audit log. Requires an admin JWT.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="SuccessResponse"/> on success,
    /// or <c>404</c> if the user does not exist.
    /// </returns>
    [HttpDelete("users/{username}")]
    public async Task<IActionResult> DeleteUser(string username)
    {
        string decodedUsername = Uri.UnescapeDataString(username);
        UserRecord? user = await _db.GetUserAsync(decodedUsername);
        if (user == null)
        {
            return NotFound(new ErrorResponse { Error = $"User '{decodedUsername}' not found." });
        }

        await _db.DeleteUserAsync(decodedUsername);

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        string actingUser = User.Identity!.Name!;
        await _auditService.LogUserDisableAsync(actingUser, decodedUsername, ip);

        return Ok(new SuccessResponse());
    }

    /// <summary>
    /// Assigns a role to a principal on a package (admin override).
    /// </summary>
    /// <param name="scope">The bare scope name (without the leading <c>@</c>).</param>
    /// <param name="name">The package's local name within the scope.</param>
    /// <remarks>
    /// Reads an <see cref="AssignRoleRequest"/> JSON body specifying the principal type,
    /// principal identifier, and role to assign. Requires an admin JWT.
    /// This replaces the legacy <c>PUT /admin/packages/{name}/owners</c> endpoint.
    /// </remarks>
    /// <returns>
    /// <c>200</c> on success,
    /// <c>400</c> if the request body is malformed or the role/principal type is invalid,
    /// or <c>404</c> if the package does not exist.
    /// </returns>
    [HttpPut("packages/{scope}/{name}/roles")]
    public async Task<IActionResult> AdminAssignRole(string scope, string name)
    {
        string packageName = $"@{Uri.UnescapeDataString(scope)}/{Uri.UnescapeDataString(name)}";
        string username = User.Identity!.Name!;

        if (!await _db.PackageExistsAsync(packageName))
        {
            return NotFound(new ErrorResponse { Error = $"Package '{packageName}' not found." });
        }

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

        string[] validPrincipalTypes = ["user", "team", "org"];
        if (!Array.Exists(validPrincipalTypes, p => p == body.PrincipalType))
            return BadRequest(new ErrorResponse { Error = $"Invalid principal_type '{body.PrincipalType}'. Must be one of: user, team, org." });

        string[] validRoles = ["owner", "maintainer", "publisher", "reader"];
        if (!Array.Exists(validRoles, r => r == body.Role))
            return BadRequest(new ErrorResponse { Error = $"Invalid role '{body.Role}'. Must be one of: owner, maintainer, publisher, reader." });

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _db.AssignPackageRoleAsync(packageName, body.PrincipalType, body.PrincipalId, body.Role);
        await _auditService.LogRoleAssignAsync(packageName, username, body.PrincipalId, ip);

        return Ok(new SuccessResponse());
    }

    /// <summary>
    /// Returns a paginated list of audit log entries.
    /// </summary>
    /// <remarks>
    /// Accepts the following optional query-string parameters:
    /// <list type="bullet">
    ///   <item><term>page</term><description>1-based page number (default: 1).</description></item>
    ///   <item><term>pageSize</term><description>Results per page, 1–200 (default: 50).</description></item>
    ///   <item><term>package</term><description>Filter to entries for a specific package name.</description></item>
    ///   <item><term>action</term><description>Filter to entries with a specific action string.</description></item>
    /// </list>
    /// Requires an admin JWT.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with an <see cref="AuditLogResponse"/> containing the matching
    /// <see cref="AuditEntry"/> items and pagination metadata.
    /// </returns>
    [HttpGet("audit-log")]
    public async Task<IActionResult> GetAuditLog()
    {
        int page = 1;
        int pageSize = 50;
        string? packageFilter = null;
        string? actionFilter = null;

        if (Request.Query.TryGetValue("page", out var pageStr) && int.TryParse(pageStr, out int parsedPage) && parsedPage > 0)
        {
            page = parsedPage;
        }

        if (Request.Query.TryGetValue("pageSize", out var pageSizeStr) && int.TryParse(pageSizeStr, out int parsedPageSize) && parsedPageSize > 0)
        {
            pageSize = Math.Min(parsedPageSize, 200);
        }

        if (Request.Query.TryGetValue("package", out var pkgFilter) && !string.IsNullOrEmpty(pkgFilter))
        {
            packageFilter = pkgFilter.ToString();
        }

        if (Request.Query.TryGetValue("action", out var actFilter) && !string.IsNullOrEmpty(actFilter))
        {
            actionFilter = actFilter.ToString();
        }

        var result = await _auditService.GetAuditLogAsync(page, pageSize, packageFilter, actionFilter);
        int totalPages = (int)Math.Ceiling(result.TotalCount / (double)pageSize);

        return Ok(new AuditLogResponse
        {
            Entries = result.Items,
            TotalCount = result.TotalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        });
    }
}
