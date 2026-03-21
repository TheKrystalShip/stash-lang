using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Auth;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Endpoints;
using Stash.Registry.Services;

namespace Stash.Registry.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly JwtTokenService _jwtService;
    private readonly IAuthProvider _authProvider;
    private readonly AuditService _auditService;
    private readonly RegistryConfig _config;

    public AuthController(
        IRegistryDatabase db,
        JwtTokenService jwtService,
        IAuthProvider authProvider,
        AuditService auditService,
        RegistryConfig config)
    {
        _db = db;
        _jwtService = jwtService;
        _authProvider = authProvider;
        _auditService = auditService;
        _config = config;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login()
    {
        LoginRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<LoginRequest>(Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? username = body?.Username?.Trim();
        string? password = body?.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return BadRequest(new ErrorResponse { Error = "Username and password are required." });
        }

        if (!await _authProvider.AuthenticateAsync(username, password))
        {
            return Unauthorized(new ErrorResponse { Error = "Invalid username or password." });
        }

        var user = await _db.GetUserAsync(username);
        string role = user?.Role ?? "user";
        string tokenId = Guid.NewGuid().ToString();
        DateTime expiresAt = AuthHelper.ParseTokenExpiry(_config.Auth.TokenExpiry);
        string jwt = _jwtService.CreateToken(username, role, "publish", expiresAt, tokenId);

        await _db.CreateTokenAsync(new TokenRecord
        {
            Id = tokenId,
            Username = username,
            TokenHash = "",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        });

        return Ok(new LoginResponse { Token = jwt, ExpiresAt = expiresAt });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register()
    {
        if (!_config.Auth.RegistrationEnabled)
        {
            return StatusCode(403, new ErrorResponse { Error = "Registration is disabled." });
        }

        RegisterRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<RegisterRequest>(Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string? username = body?.Username?.Trim();
        string? password = body?.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return BadRequest(new ErrorResponse { Error = "Username and password are required." });
        }

        if (username.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_-]+$"))
        {
            return BadRequest(new ErrorResponse { Error = "Username must be 1-64 characters and contain only letters, digits, hyphens, or underscores." });
        }

        if (password.Length < 8)
        {
            return BadRequest(new ErrorResponse { Error = "Password must be at least 8 characters." });
        }

        try
        {
            await _authProvider.CreateUserAsync(username, password);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogUserCreateAsync(username, ip);

        return StatusCode(201, new RegisterResponse { Username = username });
    }

    [HttpGet("whoami")]
    [Authorize]
    public async Task<IActionResult> Whoami()
    {
        string username = User.Identity!.Name!;
        string role = User.FindFirstValue(ClaimTypes.Role) ?? "user";

        return Ok(new WhoamiResponse { Username = username, Role = role });
    }

    [HttpPost("tokens")]
    [Authorize]
    public async Task<IActionResult> CreateToken()
    {
        string username = User.Identity!.Name!;
        string role = User.FindFirstValue(ClaimTypes.Role) ?? "user";

        TokenCreateRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<TokenCreateRequest>(Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        string scope = string.IsNullOrEmpty(body?.Scope) ? "publish" : body.Scope;

        if (!string.Equals(scope, "read", StringComparison.Ordinal)
            && !string.Equals(scope, "publish", StringComparison.Ordinal)
            && !string.Equals(scope, "admin", StringComparison.Ordinal))
        {
            return BadRequest(new ErrorResponse { Error = "Scope must be 'read', 'publish', or 'admin'." });
        }

        if (string.Equals(scope, "admin", StringComparison.Ordinal)
            && !string.Equals(role, "admin", StringComparison.Ordinal))
        {
            return StatusCode(403, new ErrorResponse { Error = "Only admin users can create admin-scoped tokens." });
        }

        string tokenId = Guid.NewGuid().ToString();
        DateTime expiresAt = AuthHelper.ParseTokenExpiry(_config.Auth.TokenExpiry);
        string jwt = _jwtService.CreateToken(username, role, scope, expiresAt, tokenId);

        await _db.CreateTokenAsync(new TokenRecord
        {
            Id = tokenId,
            Username = username,
            TokenHash = "",
            Scope = scope,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            Description = body?.Description
        });

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogTokenCreateAsync(username, ip);

        return StatusCode(201, new TokenCreateResponse { Token = jwt, TokenId = tokenId, Scope = scope, ExpiresAt = expiresAt, Description = body?.Description });
    }

    [HttpDelete("tokens/{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteToken(string id)
    {
        string username = User.Identity!.Name!;
        string role = User.FindFirstValue(ClaimTypes.Role) ?? "user";

        TokenRecord? record = await _db.GetTokenByIdAsync(id);
        if (record == null)
        {
            var userTokens = await _db.GetUserTokensAsync(username);
            record = userTokens.Find(t => string.Equals(t.Id, id, StringComparison.Ordinal));
        }

        if (record == null)
        {
            return NotFound(new ErrorResponse { Error = "Token not found." });
        }

        bool isOwner = string.Equals(record.Username, username, StringComparison.Ordinal);
        bool isAdmin = string.Equals(role, "admin", StringComparison.Ordinal);

        if (!isOwner && !isAdmin)
        {
            return StatusCode(403, new ErrorResponse { Error = "You do not have permission to delete this token." });
        }

        await _db.DeleteTokenAsync(record.Id);

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogTokenRevokeAsync(username, record.Id, ip);

        return Ok(new SuccessResponse());
    }
}
