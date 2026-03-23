using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Stash.Registry.Configuration;

namespace Stash.Registry.Auth;

/// <summary>
/// Creates and validates HMAC-SHA-256 signed JSON Web Tokens for the Stash Package Registry.
/// </summary>
/// <remarks>
/// <para>
/// A single instance is registered as a singleton in the DI container by
/// <see cref="Startup.ConfigureServices"/>. The signing key is either loaded from
/// <c>Registry:Security:JwtSigningKey</c> in configuration (minimum 32 UTF-8 bytes / 256 bits)
/// or auto-generated with <see cref="System.Security.Cryptography.RandomNumberGenerator"/> for
/// development convenience.
/// </para>
/// <para>
/// <b>Production note:</b> when no key is configured a warning is emitted to
/// <see cref="Console"/> and tokens will not survive process restarts. Always set
/// <c>Registry:Security:JwtSigningKey</c> in production deployments.
/// </para>
/// <para>
/// Claims embedded in every issued token:
/// <list type="table">
///   <listheader><term>Claim</term><description>Description</description></listheader>
///   <item><term><c>sub</c> (<see cref="System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub"/>)</term><description>The username of the authenticated user.</description></item>
///   <item><term><c>jti</c> (<see cref="System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti"/>)</term><description>A unique token identifier used for revocation checks in <see cref="Database.IRegistryDatabase"/>.</description></item>
///   <item><term><c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name</c></term><description>Alias for the username; mapped via <see cref="Microsoft.IdentityModel.Tokens.TokenValidationParameters.NameClaimType"/>.</description></item>
///   <item><term><c>http://schemas.microsoft.com/ws/2008/06/identity/claims/role</c></term><description>The user role (e.g. <c>"user"</c> or <c>"admin"</c>); mapped via <see cref="Microsoft.IdentityModel.Tokens.TokenValidationParameters.RoleClaimType"/>.</description></item>
///   <item><term><c>token_scope</c></term><description>The permission scope: <c>"publish"</c>, <c>"read"</c>, or <c>"admin"</c>. Evaluated by authorization policies in <see cref="Startup.ConfigureServices"/>.</description></item>
///   <item><term><c>exp</c></term><description>Token expiry as a Unix timestamp; controlled by the <paramref name="expiresAt"/> parameter of <see cref="CreateToken"/>.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class JwtTokenService
{
    /// <summary>
    /// The issuer identifier embedded in every token and validated on incoming requests.
    /// </summary>
    public const string Issuer = "StashRegistry";

    /// <summary>
    /// The audience identifier embedded in every token and validated on incoming requests.
    /// </summary>
    public const string Audience = "StashRegistry";

    /// <summary>
    /// The HMAC-SHA-256 signing key derived from configuration or auto-generated at startup.
    /// </summary>
    private readonly SymmetricSecurityKey _signingKey;

    /// <summary>
    /// Initialises a new <see cref="JwtTokenService"/> using the signing key from
    /// <paramref name="config"/>.
    /// </summary>
    /// <param name="config">
    /// The registry configuration. <see cref="Configuration.SecurityConfig.JwtSigningKey"/> is
    /// read to obtain the signing key. If <see langword="null"/> or empty, a random key is
    /// generated for the lifetime of the process (development only).
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a key is configured but is shorter than 32 characters (256 bits).
    /// </exception>
    public JwtTokenService(RegistryConfig config)
    {
        string? keyText = config.Security.JwtSigningKey;
        byte[] keyBytes;
        if (string.IsNullOrEmpty(keyText))
        {
            keyBytes = RandomNumberGenerator.GetBytes(32);
            Console.WriteLine("WARNING: No JWT signing key configured. Using auto-generated key (tokens will not survive restarts). Set 'Registry:Security:JwtSigningKey' in appsettings.json for production.");
        }
        else
        {
            keyBytes = Encoding.UTF8.GetBytes(keyText);
            if (keyBytes.Length < 32)
            {
                throw new InvalidOperationException(
                    "JWT signing key must be at least 32 characters (256 bits).");
            }
        }
        _signingKey = new SymmetricSecurityKey(keyBytes);
    }

    /// <summary>
    /// Creates a signed JWT containing the standard registry claims.
    /// </summary>
    /// <param name="username">
    /// The authenticated username; written to <c>sub</c> and <c>name</c> claims.
    /// </param>
    /// <param name="role">
    /// The user's role (e.g. <c>"user"</c> or <c>"admin"</c>); written to the <c>role</c> claim
    /// and used by <c>RequireAdmin</c> authorization policy.
    /// </param>
    /// <param name="scope">
    /// The token's permission scope (<c>"read"</c>, <c>"publish"</c>, or <c>"admin"</c>);
    /// written to the <c>token_scope</c> claim and evaluated by authorization policies
    /// <c>RequirePublishScope</c> and <c>RequireAdminScope</c>.
    /// </param>
    /// <param name="expiresAt">
    /// The absolute UTC expiry time; written to the <c>exp</c> claim. Tokens presented after
    /// this time are rejected by the JWT Bearer middleware.
    /// </param>
    /// <param name="tokenId">
    /// A unique identifier for the token (typically a <see cref="System.Guid"/> string);
    /// written to the <c>jti</c> claim and stored in <see cref="Database.IRegistryDatabase"/>
    /// to support revocation checks performed in <see cref="Startup.ConfigureServices"/>.
    /// </param>
    /// <returns>A compact-serialised JWT string suitable for use as a Bearer token.</returns>
    public string CreateToken(string username, string role, string scope, DateTime expiresAt, string tokenId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, tokenId),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim("token_scope", scope)
        };

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Builds the <see cref="Microsoft.IdentityModel.Tokens.TokenValidationParameters"/> used by
    /// the JWT Bearer authentication middleware registered in <see cref="Startup.ConfigureServices"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="Microsoft.IdentityModel.Tokens.TokenValidationParameters"/> instance configured to:
    /// <list type="bullet">
    ///   <item><description>Validate issuer against <see cref="Issuer"/>.</description></item>
    ///   <item><description>Validate audience against <see cref="Audience"/>.</description></item>
    ///   <item><description>Enforce token lifetime with a 30-second clock-skew allowance.</description></item>
    ///   <item><description>Validate the HMAC-SHA-256 signature using <see cref="_signingKey"/>.</description></item>
    ///   <item><description>Map <c>name</c> and <c>role</c> claims to ASP.NET Core identity conventions.</description></item>
    /// </list>
    /// </returns>
    public TokenValidationParameters GetValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    }
}
