using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Stash.Registry.Auth;
using Stash.Registry.Configuration;

namespace Stash.Tests.Registry;

public sealed class JwtTokenServiceTests
{
    private static JwtTokenService CreateService(string? signingKey = null)
    {
        var config = new RegistryConfig
        {
            Security = new SecurityConfig
            {
                JwtSigningKey = signingKey ?? "test-signing-key-that-is-at-least-32-chars-long!"
            }
        };
        return new JwtTokenService(config);
    }

    [Fact]
    public void CreateToken_WithoutMachineId_DoesNotIncludeClaim()
    {
        var service = CreateService();
        string token = service.CreateToken("alice", "user", "publish",
            DateTime.UtcNow.AddHours(1), "token-1");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Null(jwt.Claims.FirstOrDefault(c => c.Type == "machine_id"));
    }

    [Fact]
    public void CreateToken_WithMachineId_IncludesClaim()
    {
        var service = CreateService();
        string machineId = "abc123def456";
        string token = service.CreateToken("alice", "user", "publish",
            DateTime.UtcNow.AddHours(1), "token-1", machineId);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var claim = jwt.Claims.FirstOrDefault(c => c.Type == "machine_id");
        Assert.NotNull(claim);
        Assert.Equal(machineId, claim.Value);
    }

    [Fact]
    public void CreateToken_ContainsStandardClaims()
    {
        var service = CreateService();
        string token = service.CreateToken("bob", "admin", "admin",
            DateTime.UtcNow.AddHours(1), "token-2");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("bob", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("token-2", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value);
        Assert.Equal("admin", jwt.Claims.First(c => c.Type == "token_scope").Value);
    }

    [Fact]
    public void GetExpiredTokenValidationParameters_DoesNotValidateLifetime()
    {
        var service = CreateService();
        var parameters = service.GetExpiredTokenValidationParameters();

        Assert.False(parameters.ValidateLifetime);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateIssuerSigningKey);
    }

    [Fact]
    public void GetValidationParameters_ValidatesLifetime()
    {
        var service = CreateService();
        var parameters = service.GetValidationParameters();

        Assert.True(parameters.ValidateLifetime);
    }

    [Fact]
    public void GetExpiredTokenValidationParameters_CanValidateExpiredToken()
    {
        var service = CreateService();

        // CreateToken enforces expires > notBefore at the JWT library level, so we build
        // an already-expired token directly to test that ValidateLifetime=false accepts it.
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("test-signing-key-that-is-at-least-32-chars-long!"));
        var past = DateTime.UtcNow.AddMinutes(-5);
        var handler = new JwtSecurityTokenHandler();
        var rawToken = handler.CreateJwtSecurityToken(
            issuer: JwtTokenService.Issuer,
            audience: JwtTokenService.Audience,
            subject: new ClaimsIdentity([new Claim(ClaimTypes.Name, "charlie")]),
            notBefore: past.AddSeconds(-10),
            expires: past,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        string token = handler.WriteToken(rawToken);

        var parameters = service.GetExpiredTokenValidationParameters();

        // Should NOT throw — lifetime validation is disabled
        var principal = handler.ValidateToken(token, parameters, out _);
        Assert.Equal("charlie", principal.Identity!.Name);
    }

    [Fact]
    public void Constructor_ThrowsWhenKeyTooShort()
    {
        Assert.Throws<InvalidOperationException>(() => CreateService("short"));
    }
}
