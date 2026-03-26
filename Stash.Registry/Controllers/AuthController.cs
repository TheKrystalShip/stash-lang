using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Stash.Registry.Auth;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Endpoints;
using Stash.Registry.Services;

namespace Stash.Registry.Controllers;

/// <summary>
/// REST API controller for authentication and token management.
/// </summary>
/// <remarks>
/// <para>
/// Handles user login, self-service registration, and JWT token lifecycle. The
/// <c>login</c> and <c>register</c> endpoints are anonymous; all token management
/// endpoints require a valid JWT. On login a <c>publish</c>-scoped token is issued
/// automatically; additional tokens with <c>read</c>, <c>publish</c>, or <c>admin</c>
/// scope can be created via <c>POST /api/v1/auth/tokens</c>.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IRegistryDatabase _db;
    private readonly JwtTokenService _jwtService;
    private readonly IAuthProvider _authProvider;
    private readonly AuditService _auditService;
    private readonly RegistryConfig _config;

    /// <summary>
    /// Initialises the controller with its required services.
    /// </summary>
    /// <param name="db">Registry database used to look up users and persist tokens.</param>
    /// <param name="jwtService">Service that mints signed JWT strings.</param>
    /// <param name="authProvider">Provider that validates credentials and creates user accounts.</param>
    /// <param name="auditService">Service that records security-relevant events.</param>
    /// <param name="config">Registry-wide configuration, including token expiry settings.</param>
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

    /// <summary>
    /// Authenticates a user with username and password and returns a JWT.
    /// </summary>
    /// <remarks>
    /// Reads a <see cref="LoginRequest"/> JSON body. On success a <c>publish</c>-scoped
    /// JWT is issued and persisted as a <see cref="TokenRecord"/>. No authentication
    /// is required to call this endpoint.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="LoginResponse"/> containing the JWT and its expiry,
    /// <c>400</c> if the request body is missing or malformed,
    /// or <c>401</c> if the credentials are invalid.
    /// </returns>
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
        string? machineId = Request.Headers["X-Machine-Id"].FirstOrDefault();

        // Create short-lived access token
        string accessTokenId = Guid.NewGuid().ToString();
        DateTime accessExpiresAt = AuthHelper.ParseTokenExpiry(_config.Auth.AccessTokenExpiry);
        string accessJwt = _jwtService.CreateToken(username, role, "publish", accessExpiresAt, accessTokenId, machineId);

        await _db.CreateTokenAsync(new TokenRecord
        {
            Id = accessTokenId,
            Username = username,
            TokenHash = "",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = accessExpiresAt
        });

        // Create long-lived refresh token
        string? refreshTokenString = null;
        DateTime? refreshExpiresAt = null;
        if (!string.IsNullOrEmpty(machineId))
        {
            refreshTokenString = GenerateRefreshToken();
            string refreshTokenHash = HashToken(refreshTokenString);
            refreshExpiresAt = AuthHelper.ParseTokenExpiry(_config.Auth.RefreshTokenExpiry);

            await _db.CreateRefreshTokenAsync(new Database.Models.RefreshTokenRecord
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                TokenHash = refreshTokenHash,
                AccessTokenId = accessTokenId,
                FamilyId = accessTokenId,
                MachineId = machineId,
                Scope = "publish",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = refreshExpiresAt.Value
            });
        }

        return Ok(new LoginResponse
        {
            Token = accessJwt,
            ExpiresAt = accessExpiresAt,
            RefreshToken = refreshTokenString,
            RefreshTokenExpiresAt = refreshExpiresAt
        });
    }

    /// <summary>
    /// Creates a new user account (self-service registration).
    /// </summary>
    /// <remarks>
    /// Reads a <see cref="RegisterRequest"/> JSON body. Registration can be disabled
    /// globally via <see cref="RegistryConfig"/>; if so, <c>403</c> is returned.
    /// Usernames must be 1–64 alphanumeric, hyphen, or underscore characters.
    /// Passwords must be at least 8 characters. No authentication is required.
    /// </remarks>
    /// <returns>
    /// <c>201</c> with a <see cref="RegisterResponse"/>,
    /// <c>400</c> if validation fails,
    /// <c>403</c> if registration is disabled,
    /// or <c>409</c> if the username is already taken.
    /// </returns>
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

    /// <summary>
    /// Returns the identity of the currently authenticated user.
    /// </summary>
    /// <remarks>
    /// Requires a valid JWT. The username and role are read directly from the JWT claims;
    /// no database query is performed.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="WhoamiResponse"/> containing the username and role,
    /// or <c>401</c> if the request is unauthenticated.
    /// </returns>
    [HttpGet("whoami")]
    [Authorize]
    public async Task<IActionResult> Whoami()
    {
        string username = User.Identity!.Name!;
        string role = User.FindFirstValue(ClaimTypes.Role) ?? "user";

        return Ok(new WhoamiResponse { Username = username, Role = role });
    }

    /// <summary>
    /// Creates a new API token for the authenticated user.
    /// </summary>
    /// <remarks>
    /// Reads a <see cref="TokenCreateRequest"/> JSON body. The <c>scope</c> field must be
    /// <c>read</c>, <c>publish</c>, or <c>admin</c>; only users with the <c>admin</c> role
    /// may request an admin-scoped token. The resulting JWT is persisted as a
    /// <see cref="TokenRecord"/> and returned once — it cannot be retrieved again.
    /// </remarks>
    /// <returns>
    /// <c>201</c> with a <see cref="TokenCreateResponse"/> containing the JWT and token ID,
    /// <c>400</c> if the body is invalid or the scope is unrecognised,
    /// <c>401</c> if unauthenticated,
    /// or <c>403</c> if a non-admin requests an admin-scoped token.
    /// </returns>
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
        DateTime expiresAt = AuthHelper.ParseTokenExpiry(_config.Auth.ApiTokenExpiry);
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

    /// <summary>
    /// Revokes (deletes) a token by its ID.
    /// </summary>
    /// <param name="id">The token ID to revoke.</param>
    /// <remarks>
    /// A user may revoke their own tokens. Admin users may revoke any token. The
    /// deletion is recorded in the audit log.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="SuccessResponse"/> on success,
    /// <c>401</c> if unauthenticated,
    /// <c>403</c> if the caller does not own the token and is not an admin,
    /// or <c>404</c> if the token does not exist.
    /// </returns>
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

    /// <summary>
    /// Exchanges a valid refresh token for a new access/refresh token pair.
    /// </summary>
    /// <remarks>
    /// Implements OAuth2-style token rotation. The refresh token is consumed on use,
    /// preventing replay attacks. The client must present the same machine fingerprint
    /// that was used during login. If a consumed refresh token is presented, all tokens
    /// in the family are revoked as a security measure.
    /// </remarks>
    [HttpPost("tokens/refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken()
    {
        RefreshTokenRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<RefreshTokenRequest>(Request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse { Error = "Invalid JSON body." });
        }

        if (string.IsNullOrEmpty(body?.RefreshToken) || string.IsNullOrEmpty(body?.AccessToken) || string.IsNullOrEmpty(body?.MachineId))
        {
            return BadRequest(new ErrorResponse { Error = "refreshToken, accessToken, and machineId are required." });
        }

        // Validate the expired access token's signature (but not lifetime)
        ClaimsPrincipal? principal;
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            principal = tokenHandler.ValidateToken(body.AccessToken, _jwtService.GetExpiredTokenValidationParameters(), out _);
        }
        catch (SecurityTokenException)
        {
            return Unauthorized(new ErrorResponse { Error = "Invalid access token." });
        }

        string? tokenUsername = principal.Identity?.Name;
        string? tokenJti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (string.IsNullOrEmpty(tokenUsername) || string.IsNullOrEmpty(tokenJti))
        {
            return Unauthorized(new ErrorResponse { Error = "Invalid access token claims." });
        }

        // Cross-validate machine_id claim from the JWT against the request body
        string? tokenMachineId = principal.FindFirstValue("machine_id");
        if (!string.Equals(tokenMachineId, body.MachineId, StringComparison.Ordinal))
        {
            return Unauthorized(new ErrorResponse { Error = "Machine fingerprint mismatch." });
        }

        // Look up the refresh token by hash
        string refreshTokenHash = HashToken(body.RefreshToken);
        var refreshRecord = await _db.GetRefreshTokenByHashAsync(refreshTokenHash);
        if (refreshRecord == null)
        {
            return Unauthorized(new ErrorResponse { Error = "Invalid refresh token." });
        }

        // Verify the access token is the one paired with this refresh token
        if (!string.Equals(tokenJti, refreshRecord.AccessTokenId, StringComparison.Ordinal))
        {
            return Unauthorized(new ErrorResponse { Error = "Token pair mismatch." });
        }

        // Check if refresh token has been consumed (possible token theft)
        if (refreshRecord.Consumed)
        {
            await RevokeTokenFamilyAsync(refreshRecord.FamilyId, refreshRecord.Username);
            return Unauthorized(new ErrorResponse { Error = "Refresh token has already been used. All tokens have been revoked for security." });
        }

        // Validate refresh token belongs to the right user
        if (!string.Equals(refreshRecord.Username, tokenUsername, StringComparison.Ordinal))
        {
            return Unauthorized(new ErrorResponse { Error = "Token mismatch." });
        }

        // Validate machine fingerprint matches
        if (!string.Equals(refreshRecord.MachineId, body.MachineId, StringComparison.Ordinal))
        {
            return Unauthorized(new ErrorResponse { Error = "Machine fingerprint mismatch. Please log in again." });
        }

        // Check refresh token hasn't expired
        if (refreshRecord.ExpiresAt < DateTime.UtcNow)
        {
            return Unauthorized(new ErrorResponse { Error = "Refresh token has expired. Please log in again." });
        }

        // Atomically consume the old refresh token (rotation)
        bool consumed = await _db.ConsumeRefreshTokenAsync(refreshRecord.Id);
        if (!consumed)
        {
            await RevokeTokenFamilyAsync(refreshRecord.FamilyId, refreshRecord.Username);
            return Unauthorized(new ErrorResponse { Error = "Refresh token has already been used. All tokens have been revoked for security." });
        }

        // Look up the user to get current role
        var user = await _db.GetUserAsync(tokenUsername);
        string role = user?.Role ?? "user";

        // Delete the old access token record
        await _db.DeleteTokenAsync(tokenJti);

        // Create new access token
        string newAccessTokenId = Guid.NewGuid().ToString();
        DateTime newAccessExpiresAt = AuthHelper.ParseTokenExpiry(_config.Auth.AccessTokenExpiry);
        string newAccessJwt = _jwtService.CreateToken(tokenUsername, role, refreshRecord.Scope, newAccessExpiresAt, newAccessTokenId, body.MachineId);

        await _db.CreateTokenAsync(new TokenRecord
        {
            Id = newAccessTokenId,
            Username = tokenUsername,
            TokenHash = "",
            Scope = refreshRecord.Scope,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = newAccessExpiresAt
        });

        // Create new refresh token (rotation)
        string newRefreshTokenString = GenerateRefreshToken();
        string newRefreshTokenHash = HashToken(newRefreshTokenString);
        DateTime newRefreshExpiresAt = AuthHelper.ParseTokenExpiry(_config.Auth.RefreshTokenExpiry);

        await _db.CreateRefreshTokenAsync(new Database.Models.RefreshTokenRecord
        {
            Id = Guid.NewGuid().ToString(),
            Username = tokenUsername,
            TokenHash = newRefreshTokenHash,
            AccessTokenId = newAccessTokenId,
            FamilyId = refreshRecord.FamilyId,
            MachineId = body.MachineId,
            Scope = refreshRecord.Scope,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = newRefreshExpiresAt
        });

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogTokenRefreshAsync(tokenUsername, ip);

        return Ok(new RefreshTokenResponse
        {
            AccessToken = newAccessJwt,
            RefreshToken = newRefreshTokenString,
            ExpiresAt = newAccessExpiresAt,
            RefreshTokenExpiresAt = newRefreshExpiresAt
        });
    }

    /// <summary>
    /// Revokes an entire refresh token family and their associated access tokens.
    /// </summary>
    private async Task RevokeTokenFamilyAsync(string familyId, string username)
    {
        var familyTokens = await _db.GetRefreshTokensByFamilyAsync(familyId);
        foreach (var ft in familyTokens)
        {
            await _db.DeleteTokenAsync(ft.AccessTokenId);
        }
        await _db.DeleteRefreshTokensByFamilyAsync(familyId);

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogTokenTheftDetectedAsync(username, familyId, ip);
    }

    /// <summary>
    /// Generates a cryptographically random refresh token string.
    /// </summary>
    private static string GenerateRefreshToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Computes the SHA-256 hex digest of a token string.
    /// </summary>
    private static string HashToken(string token)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }
}
