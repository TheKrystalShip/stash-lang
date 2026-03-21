using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Stash.Registry.Configuration;

namespace Stash.Registry.Auth;

public sealed class JwtTokenService
{
    public const string Issuer = "StashRegistry";
    public const string Audience = "StashRegistry";

    private readonly SymmetricSecurityKey _signingKey;

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
