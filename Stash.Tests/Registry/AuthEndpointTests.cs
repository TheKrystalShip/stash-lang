using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Auth;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Integration tests for auth endpoint shape: accessToken field, expiresIn, and
/// structured registration-disabled body.
/// </summary>
public sealed class AuthEndpointTests : IDisposable
{
    private SqliteConnection? _connection;

    public void Dispose() => _connection?.Dispose();

    /// <summary>Creates a factory with Development environment (RegistrationEnabled=true via appsettings.Development.json).</summary>
    private WebApplicationFactory<Stash.Registry.Program> CreateDevFactory()
    {
        _connection?.Dispose();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var conn = _connection;

        return new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                // Pin content root to an absolute path so the factory is cwd-independent
                // (parallel cwd-mutating tests would otherwise break relative resolution).
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(DbContextOptions<RegistryDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddDbContext<RegistryDbContext>(options =>
                        options.UseSqlite(conn));

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });
    }

    /// <summary>Creates a factory with RegistrationEnabled explicitly set to false.</summary>
    private WebApplicationFactory<Stash.Registry.Program> CreateRegistrationDisabledFactory()
    {
        _connection?.Dispose();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var conn = _connection;

        return new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                // Pin content root to an absolute path so the factory is cwd-independent.
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                // Use non-development environment so appsettings.Development.json override doesn't apply.
                builder.UseSetting("environment", "Testing");
                // Explicitly disable registration.
                builder.UseSetting("Registry:Auth:RegistrationEnabled", "false");
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(DbContextOptions<RegistryDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddDbContext<RegistryDbContext>(options =>
                        options.UseSqlite(conn));

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });
    }

    private static StringContent Json(object body) =>
        new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Login_Response_IncludesAccessTokenAndDeprecatedToken()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        // Register a user first (registration enabled in Development env).
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "tokentest", password = "password123" }));

        var response = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "tokentest", password = "password123" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("accessToken", out var accessToken), "Response must include 'accessToken' field.");
        Assert.False(string.IsNullOrEmpty(accessToken.GetString()), "'accessToken' must not be empty.");

        Assert.True(root.TryGetProperty("token", out var deprecatedToken), "Response must include deprecated 'token' alias.");
        Assert.Equal(accessToken.GetString(), deprecatedToken.GetString());
    }

    [Fact]
    public async Task Login_Response_IncludesExpiresIn()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "expirestest", password = "password123" }));

        var response = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "expirestest", password = "password123" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("expiresIn", out var expiresIn), "Response must include 'expiresIn' field.");
        Assert.Equal(JsonValueKind.Number, expiresIn.ValueKind);
        Assert.True(expiresIn.GetInt32() > 0, "'expiresIn' must be a positive integer (seconds).");
    }

    [Fact]
    public async Task Register_WhenDisabled_Returns403WithStructuredBody()
    {
        await using var factory = CreateRegistrationDisabledFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "newuser", password = "password123" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("error", out var error), "Response must include 'error' field.");
        Assert.Equal("registration_disabled", error.GetString());

        Assert.True(root.TryGetProperty("message", out var message), "Response must include 'message' field.");
        Assert.False(string.IsNullOrEmpty(message.GetString()), "'message' must not be empty.");
    }

    // ── A2: audit instrumentation tests ──────────────────────────────────────

    /// <summary>Helper: read all audit entries from the test factory's in-process DB.</summary>
    private static async Task<List<AuditEntry>> GetAuditEntriesAsync(
        WebApplicationFactory<Stash.Registry.Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        var result = await db.GetAuditLogAsync(1, 1000, null, null);
        return result.Items;
    }

    [Fact]
    public async Task Login_Success_WritesExactlyOneLoginSuccessAuditEntry()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "audit-login-ok", password = "Password123!" }));

        var response = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "audit-login-ok", password = "Password123!" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entries = await GetAuditEntriesAsync(factory);
        var loginSuccessEntries = entries
            .Where(e => e.Action == AuditActions.AuthLoginSuccess && e.User == "audit-login-ok")
            .ToList();

        Assert.Single(loginSuccessEntries);
        Assert.Equal("allow", loginSuccessEntries[0].Decision);
    }

    [Fact]
    public async Task Login_BadCredentials_WritesExactlyOneLoginFailureAuditEntry()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "audit-login-fail", password = "Password123!" }));

        var response = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "audit-login-fail", password = "wrongpassword" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var entries = await GetAuditEntriesAsync(factory);
        var failureEntries = entries
            .Where(e => e.Action == AuditActions.AuthLoginFailure && e.User == "audit-login-fail")
            .ToList();

        Assert.Single(failureEntries);
        Assert.Equal("deny", failureEntries[0].Decision);
    }

    [Fact]
    public async Task RefreshToken_InvalidAccessToken_WritesExactlyOneRefreshFailureAuditEntry()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        // A structurally valid (3-part) but cryptographically invalid JWT triggers the
        // earliest failure branch (SecurityTokenException — invalid signature).
        // Note: JSON property names use camelCase per RefreshTokenRequest DTO.
        const string InvalidJwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" +
            ".eyJzdWIiOiJ0ZXN0In0" +
            ".invalid-signature-that-will-not-verify";
        var response = await client.PostAsync("/api/v1/auth/tokens/refresh",
            Json(new
            {
                accessToken = InvalidJwt,
                refreshToken = "also-fake",
                machineId = "test-machine"
            }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var entries = await GetAuditEntriesAsync(factory);
        var refreshFailEntries = entries
            .Where(e => e.Action == AuditActions.AuthRefreshFailure && e.Decision == "deny")
            .ToList();

        Assert.Single(refreshFailEntries);
    }

    [Fact]
    public async Task Register_Success_WritesTwoAuditEntries_UserCreateAndAuthRegister()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "audit-register-user", password = "Password123!" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var entries = await GetAuditEntriesAsync(factory);

        // OQ1 locked default: two entries — user.create + auth.register
        var userCreateEntries = entries
            .Where(e => e.Action == AuditActions.UserCreate && e.User == "audit-register-user")
            .ToList();
        var authRegisterEntries = entries
            .Where(e => e.Action == AuditActions.AuthRegister && e.User == "audit-register-user")
            .ToList();

        Assert.Single(userCreateEntries);
        Assert.Single(authRegisterEntries);
        Assert.Equal("allow", authRegisterEntries[0].Decision);
    }
}
