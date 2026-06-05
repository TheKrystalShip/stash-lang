using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Stash.Registry.Auth;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Endpoints;
using Stash.Registry.OpenApi;
using Stash.Registry.Services;

namespace Stash.Registry.Controllers;

/// <summary>
/// REST API controller for authentication and token management.
/// </summary>
/// <remarks>
/// <para>
/// Handles user login, self-service registration, and JWT token lifecycle. The
/// <c>login</c> and <c>register</c> endpoints are anonymous; all token management
/// endpoints require a valid JWT. On login a <c>read</c>-ceiling token is issued
/// by default (least-privilege); callers who need to publish must explicitly issue
/// a <c>publish</c> or <c>admin</c>-ceiling token via <c>POST /api/v1/auth/tokens</c>.
/// Token lifetimes are capped server-side by <c>Security.MaxTokenLifetime</c>.
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
    private readonly ILogger<AuthController> _logger;

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
        RegistryConfig config,
        ILogger<AuthController> logger)
    {
        _db = db;
        _jwtService = jwtService;
        _authProvider = authProvider;
        _auditService = auditService;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates a user with username and password and returns a JWT.
    /// </summary>
    /// <remarks>
    /// Reads a <see cref="LoginRequest"/> JSON body. On success a <c>read</c>-ceiling
    /// JWT is issued and persisted as a <see cref="TokenRecord"/>. No authentication
    /// is required to call this endpoint.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="LoginResponse"/> containing the JWT and its expiry,
    /// <c>400</c> if the request body is missing or malformed,
    /// or <c>401</c> if the credentials are invalid.
    /// </returns>
    [HttpPost("login")]
    [PublicEndpoint("login does not require a prior session — credentials are the authenticator")]
    public async Task<Results<Ok<LoginResponse>, BadRequest<ErrorResponse>, JsonUnauthorized<ErrorResponse>>> Login([FromBody] LoginRequest request)
    {
        string username = request.Username!.Trim();
        string password = request.Password!;
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!await _authProvider.AuthenticateAsync(username, password))
        {
            await _auditService.LogAuthLoginFailureAsync(username, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Invalid username or password." });
        }

        var user = await _db.GetUserAsync(username);
        string role = (user?.Role ?? UserRoles.User).ToWire();
        string? machineId = Request.Headers["X-Machine-Id"].FirstOrDefault();

        // Create short-lived access token — read-ceiling by default (least privilege).
        // Callers who need publish must issue a separate token via POST /auth/tokens.
        string accessTokenId = Guid.NewGuid().ToString();
        DateTime accessExpiresAt = AuthHelper.ParseTokenExpiry(_config.Auth.AccessTokenExpiry);
        string accessJwt = _jwtService.CreateToken(username, role, TokenScopes.Read.ToWire(), accessExpiresAt, accessTokenId, machineId);

        await _db.CreateTokenAsync(new TokenRecord
        {
            Id = accessTokenId,
            Username = username,
            TokenHash = "",
            Scope = TokenScopes.Read.ToWire(),
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
                Scope = TokenScopes.Read.ToWire(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = refreshExpiresAt.Value
            });
        }

        await _auditService.LogAuthLoginSuccessAsync(username, ip);

        return TypedResults.Ok(new LoginResponse
        {
            AccessToken = accessJwt,
            ExpiresAt = accessExpiresAt,
            ExpiresIn = (int)(accessExpiresAt - DateTime.UtcNow).TotalSeconds,
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
    [PublicEndpoint("self-service registration requires no prior account")]
    public async Task<Results<Created<RegisterResponse>, BadRequest<ErrorResponse>, JsonForbidden<ErrorResponse>, Conflict<ErrorResponse>>> Register([FromBody] RegisterRequest request)
    {
        if (!_config.Auth.RegistrationEnabled)
        {
            return new JsonForbidden<ErrorResponse>(new ErrorResponse { Error = "registration_disabled", Message = "User registration is disabled on this registry." });
        }

        string username = request.Username!.Trim();
        string password = request.Password!;

        try
        {
            string role = await _authProvider.CreateUserWithScopeAsync(username, password);
            if (role == UserRoles.Admin.ToWire())
            {
                _logger.LogInformation(
                    "User '{User}' is the first registered user and was created as admin.",
                    username);
            }
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Conflict(new ErrorResponse { Error = ex.Message });
        }

        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _auditService.LogUserCreateAsync(username, ip);
        // Two entries are intentional (OQ1 locked default): user.create is the creation record;
        // auth.register is the self-service registration event (distinct from admin-created users).
        await _auditService.LogAuthRegisterAsync(username, ip);

        return TypedResults.Created((string?)null, new RegisterResponse { Username = username });
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
    [RegistryAuthorize(RegistryAction.Whoami)]
    public Ok<WhoamiResponse> Whoami()
    {
        string username = User.Identity!.Name!;
        string rawRole = User.FindFirstValue(ClaimTypes.Role) ?? UserRoles.User.ToWire();
        UserRoles role = rawRole.ToUserRole();

        return TypedResults.Ok(new WhoamiResponse { Username = username, Role = role });
    }

    /// <summary>
    /// Lists all API tokens for the authenticated user (metadata only — no token values).
    /// </summary>
    /// <returns>
    /// <c>200</c> with a <see cref="TokenListResponse"/> containing the user's tokens,
    /// or <c>401</c> if unauthenticated.
    /// </returns>
    [HttpGet("tokens")]
    [Authorize]
    [RegistryAuthorize(RegistryAction.ListOwnTokens)]
    public async Task<Ok<TokenListResponse>> ListTokens()
    {
        string username = User.Identity!.Name!;

        List<TokenRecord> tokens = await _db.GetUserTokensAsync(username);

        var response = new TokenListResponse
        {
            Tokens = tokens.Where(t => t.ExpiresAt > DateTime.UtcNow)
                .Select(t => new TokenListItem
            {
                TokenId = t.Id,
                Scope = t.Scope.ToTokenScope(),
                Description = t.Description,
                CreatedAt = t.CreatedAt,
                ExpiresAt = t.ExpiresAt
            }).ToList()
        };

        return TypedResults.Ok(response);
    }

    /// <summary>
    /// Creates a new API token for the authenticated user.
    /// </summary>
    /// <remarks>
    /// Reads a <see cref="TokenCreateRequest"/> JSON body. The <c>ceiling</c> field must be
    /// <c>read</c>, <c>publish</c>, or <c>admin</c>; only users with the <c>admin</c> role may
    /// request an admin-ceiling token. The <c>expires_in</c> field is mandatory and capped by
    /// <c>Security.MaxTokenLifetime</c>. The resulting JWT is persisted as a
    /// <see cref="TokenRecord"/> and returned once — it cannot be retrieved again. Supplying a
    /// <c>capabilities</c> field is rejected 400 (fine-grained capability rules are deferred to
    /// a future feature).
    /// </remarks>
    /// <returns>
    /// <c>201</c> with a <see cref="TokenCreateResponse"/> containing the JWT and token ID,
    /// <c>400</c> if the body is invalid, the ceiling is absent/unrecognised, <c>expires_in</c>
    /// is absent/invalid, <c>expires_in</c> exceeds <c>MaxTokenLifetime</c>, or a
    /// <c>capabilities</c> field is supplied,
    /// <c>401</c> if unauthenticated,
    /// or <c>403</c> if a non-admin requests an admin-ceiling token.
    /// </returns>
    [HttpPost("tokens")]
    [Authorize]
    [RegistryAuthorize(RegistryAction.IssueToken)]
    public async Task<Results<Created<TokenCreateResponse>, BadRequest<ErrorResponse>, JsonForbidden<ErrorResponse>>> CreateToken([FromBody] TokenCreateRequest request)
    {
        string username = User.Identity!.Name!;
        string role = User.FindFirstValue(ClaimTypes.Role) ?? UserRoles.User.ToWire();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        // Guard against the deferred fine-grained token capability shape leaking in.
        // This is a forbid (custom error code), not a validation failure — kept inline.
        if (request.Capabilities != null)
        {
            return TypedResults.BadRequest(new ErrorResponse
            {
                Error = "capabilities_not_supported",
                Message = "Fine-grained token capabilities are not supported in this version. Remove the 'capabilities' field from your request."
            });
        }

        // ceiling: [Required] + [TokenExpiry] on the DTO cover null/empty and format.
        // The value-set check (read/publish/admin) is not expressible as a DataAnnotation
        // because Ceiling is string? (not a TokenScopes enum) — kept inline.
        string scope = request.Ceiling!;

        if (!string.Equals(scope, TokenScopes.Read.ToWire(), StringComparison.Ordinal)
            && !string.Equals(scope, TokenScopes.Publish.ToWire(), StringComparison.Ordinal)
            && !string.Equals(scope, TokenScopes.Admin.ToWire(), StringComparison.Ordinal))
        {
            return TypedResults.BadRequest(new ErrorResponse { Error = "ceiling must be 'read', 'publish', or 'admin'." });
        }

        if (string.Equals(scope, TokenScopes.Admin.ToWire(), StringComparison.Ordinal)
            && !string.Equals(role, UserRoles.Admin.ToWire(), StringComparison.Ordinal))
        {
            return new JsonForbidden<ErrorResponse>(new ErrorResponse { Error = "Only admin users can create admin-ceiling tokens." });
        }

        // [Required] + [TokenExpiry] on ExpiresIn guarantee it is present and >= 1h.
        // Re-parse here to compute the DateTime needed for token issuance.
        DateTime expiresAt = AuthHelper.ParseTokenExpiry(request.ExpiresIn!);
        TimeSpan duration = expiresAt - DateTime.UtcNow;

        TimeSpan maxLifetime = _config.Security.MaxTokenLifetime;
        if (duration > maxLifetime)
        {
            string capDisplay = $"{(int)maxLifetime.TotalDays}d";
            return TypedResults.BadRequest(new ErrorResponse
            {
                Error = "TokenLifetimeExceeded",
                Message = $"Requested lifetime exceeds the server's maximum token lifetime of {capDisplay}."
            });
        }

        string tokenId = Guid.NewGuid().ToString();
        string description = request.Name ?? request.Description ?? "";
        string jwt = _jwtService.CreateToken(username, role, scope, expiresAt, tokenId);

        await _db.CreateTokenAsync(new TokenRecord
        {
            Id = tokenId,
            Username = username,
            TokenHash = "",
            Scope = scope,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            Description = string.IsNullOrEmpty(description) ? null : description
        });

        await _auditService.LogTokenCreateAsync(username, ip);

        return TypedResults.Created((string?)null, new TokenCreateResponse
        {
            Token = jwt,
            TokenId = tokenId,
            Scope = scope.ToTokenScope(),
            ExpiresAt = expiresAt,
            Description = string.IsNullOrEmpty(description) ? null : description
        });
    }

    /// <summary>
    /// Revokes an API token by ID (soft-revocation via deletion from the token store).
    /// </summary>
    /// <param name="id">The token ID to revoke.</param>
    /// <remarks>
    /// A user may revoke their own tokens. Admin users may revoke any token. After revocation,
    /// JWTs bearing the revoked JTI are rejected 401 at the authentication layer (before the PDP)
    /// on every endpoint, including <c>[PublicEndpoint]</c> ones. The revocation is recorded in
    /// the audit log.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="SuccessResponse"/> on success,
    /// <c>401</c> if unauthenticated,
    /// <c>403</c> if the caller does not own the token and is not an admin,
    /// or <c>404</c> if the token does not exist.
    /// </returns>
    [HttpPost("tokens/{id}/revoke")]
    [Authorize]
    [RegistryAuthorize(RegistryAction.RevokeOwnToken)]
    public async Task<Results<Ok<SuccessResponse>, NotFound<ErrorResponse>>> RevokeToken(string id)
    {
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        TokenRecord? record = await _db.GetTokenByIdAsync(id);
        if (record == null)
        {
            return TypedResults.NotFound(new ErrorResponse { Error = "Token not found." });
        }

        string username = User.Identity!.Name!;
        await _db.DeleteTokenAsync(record.Id);

        await _auditService.LogTokenRevokeAsync(username, record.Id, ip);

        return TypedResults.Ok(new SuccessResponse());
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
    [RegistryAuthorize(RegistryAction.RevokeOwnToken)]
    public async Task<Results<Ok<SuccessResponse>, NotFound<ErrorResponse>>> DeleteToken(string id)
    {
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        TokenRecord? record = await _db.GetTokenByIdAsync(id);
        if (record == null)
        {
            return TypedResults.NotFound(new ErrorResponse { Error = "Token not found." });
        }

        string username = User.Identity!.Name!;
        await _db.DeleteTokenAsync(record.Id);

        await _auditService.LogTokenRevokeAsync(username, record.Id, ip);

        return TypedResults.Ok(new SuccessResponse());
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
    [PublicEndpoint("token refresh validates the refresh-token credential itself — no bearer session required")]
    public async Task<Results<Ok<RefreshTokenResponse>, BadRequest<ErrorResponse>, JsonUnauthorized<ErrorResponse>>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        // Capture IP up front so it is available in all failure branches.
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        // [Required] on RefreshToken, AccessToken, MachineId ensures all three are present.
        // Validate the expired access token's signature (but not lifetime)
        ClaimsPrincipal? principal;
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            principal = tokenHandler.ValidateToken(request.AccessToken, _jwtService.GetExpiredTokenValidationParameters(), out _);
        }
        catch (SecurityTokenException)
        {
            await _auditService.LogAuthRefreshFailureAsync(null, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Invalid access token." });
        }

        string? tokenUsername = principal.Identity?.Name;
        string? tokenJti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (string.IsNullOrEmpty(tokenUsername) || string.IsNullOrEmpty(tokenJti))
        {
            await _auditService.LogAuthRefreshFailureAsync(null, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Invalid access token claims." });
        }

        // Cross-validate machine_id claim from the JWT against the request body
        string? tokenMachineId = principal.FindFirstValue(RegistryClaims.MachineId);
        if (!string.Equals(tokenMachineId, request.MachineId, StringComparison.Ordinal))
        {
            await _auditService.LogAuthRefreshFailureAsync(tokenUsername, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Machine fingerprint mismatch." });
        }

        // Look up the refresh token by hash
        string refreshTokenHash = HashToken(request.RefreshToken!);
        var refreshRecord = await _db.GetRefreshTokenByHashAsync(refreshTokenHash);
        if (refreshRecord == null)
        {
            await _auditService.LogAuthRefreshFailureAsync(tokenUsername, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Invalid refresh token." });
        }

        // Verify the access token is the one paired with this refresh token
        if (!string.Equals(tokenJti, refreshRecord.AccessTokenId, StringComparison.Ordinal))
        {
            await _auditService.LogAuthRefreshFailureAsync(tokenUsername, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Token pair mismatch." });
        }

        // Check if refresh token has been consumed (possible token theft)
        if (refreshRecord.Consumed)
        {
            await RevokeTokenFamilyAsync(refreshRecord.FamilyId, refreshRecord.Username);
            await _auditService.LogAuthRefreshFailureAsync(refreshRecord.Username, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Refresh token has already been used. All tokens have been revoked for security." });
        }

        // Validate refresh token belongs to the right user
        if (!string.Equals(refreshRecord.Username, tokenUsername, StringComparison.Ordinal))
        {
            await _auditService.LogAuthRefreshFailureAsync(tokenUsername, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Token mismatch." });
        }

        // Validate machine fingerprint matches
        if (!string.Equals(refreshRecord.MachineId, request.MachineId, StringComparison.Ordinal))
        {
            await _auditService.LogAuthRefreshFailureAsync(tokenUsername, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Machine fingerprint mismatch. Please log in again." });
        }

        // Check refresh token hasn't expired
        if (refreshRecord.ExpiresAt < DateTime.UtcNow)
        {
            await _auditService.LogAuthRefreshFailureAsync(tokenUsername, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Refresh token has expired. Please log in again." });
        }

        // Atomically consume the old refresh token (rotation)
        bool consumed = await _db.ConsumeRefreshTokenAsync(refreshRecord.Id);
        if (!consumed)
        {
            await RevokeTokenFamilyAsync(refreshRecord.FamilyId, refreshRecord.Username);
            await _auditService.LogAuthRefreshFailureAsync(refreshRecord.Username, ip);
            return new JsonUnauthorized<ErrorResponse>(new ErrorResponse { Error = "Refresh token has already been used. All tokens have been revoked for security." });
        }

        // Look up the user to get current role
        var user = await _db.GetUserAsync(tokenUsername);
        string role = (user?.Role ?? UserRoles.User).ToWire();

        // Delete the old access token record
        await _db.DeleteTokenAsync(tokenJti);

        // Create new access token
        string newAccessTokenId = Guid.NewGuid().ToString();
        DateTime newAccessExpiresAt = AuthHelper.ParseTokenExpiry(_config.Auth.AccessTokenExpiry);
        string newAccessJwt = _jwtService.CreateToken(tokenUsername, role, refreshRecord.Scope, newAccessExpiresAt, newAccessTokenId, request.MachineId);

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
            MachineId = request.MachineId!,
            Scope = refreshRecord.Scope,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = newRefreshExpiresAt
        });

        await _auditService.LogTokenRefreshAsync(tokenUsername, ip);

        return TypedResults.Ok(new RefreshTokenResponse
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
