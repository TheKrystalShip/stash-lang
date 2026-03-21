using System;
using System.Collections.Generic;
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

[Authorize(Policy = "RequireAdmin")]
[ApiController]
[Route("api/v1/admin")]
public class AdminController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly IAuthProvider _authProvider;
    private readonly AuditService _auditService;
    private readonly RegistryConfig _config;

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

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        int users = (await _db.ListUsersAsync()).Count;
        return Ok(new StatsResponse { Users = users });
    }

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

    [HttpPut("packages/{name}/owners")]
    public async Task<IActionResult> ManageOwners(string name)
    {
        string decodedName = Uri.UnescapeDataString(name);
        string username = User.Identity!.Name!;

        if (!await _db.PackageExistsAsync(decodedName))
        {
            return NotFound(new ErrorResponse { Error = $"Package '{decodedName}' not found." });
        }

        OwnerUpdateRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<OwnerUpdateRequest>(Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (body?.Add != null)
        {
            foreach (string owner in body.Add)
            {
                await _db.AddOwnerAsync(decodedName, owner);
                await _auditService.LogOwnerAddAsync(decodedName, username, owner, ip);
            }
        }

        if (body?.Remove != null)
        {
            foreach (string owner in body.Remove)
            {
                await _db.RemoveOwnerAsync(decodedName, owner);
                await _auditService.LogOwnerRemoveAsync(decodedName, username, owner, ip);
            }
        }

        List<string> owners = await _db.GetOwnersAsync(decodedName);
        return Ok(new OwnerListResponse { Owners = owners });
    }

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
