using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry;
using Stash.Registry.Database;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Integration tests proving that the CORS middleware is inert when <c>Cors.Enabled=false</c>
/// (the default) and active when <c>Cors.Enabled=true</c> with a configured origin.
/// </summary>
public sealed class CorsMiddlewareTests : IDisposable
{
    private SqliteConnection? _connection;

    public void Dispose() => _connection?.Dispose();

    // ─────────────────────────────────────────────────────────────────────────
    // Factory helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a test factory with CORS disabled (the default).
    /// </summary>
    private WebApplicationFactory<Stash.Registry.Program> CreateDisabledFactory()
    {
        _connection?.Dispose();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var conn = _connection;

        return new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("Registry:Cors:Enabled", "false");
                builder.UseSetting("Registry:RateLimiting:Enabled", "false");
                builder.ConfigureTestServices(services =>
                {
                    ReplaceDbContext(services, conn);
                });
            });
    }

    /// <summary>
    /// Creates a test factory with CORS enabled for the specified origin.
    /// Replaces the CORS options directly via <c>ConfigureTestServices</c> so that the
    /// test-injected allowed origin is honoured regardless of <c>_config</c> snapshot ordering.
    /// </summary>
    private WebApplicationFactory<Stash.Registry.Program> CreateEnabledFactory(string allowedOrigin)
    {
        _connection?.Dispose();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var conn = _connection;

        return new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Testing");
                // Enable CORS — this flag is read from the live IConfiguration in Configure().
                builder.UseSetting("Registry:Cors:Enabled", "true");
                builder.UseSetting("Registry:RateLimiting:Enabled", "false");
                builder.ConfigureTestServices(services =>
                {
                    ReplaceDbContext(services, conn);

                    // Replace the CORS policy that was registered with the snapshot _config
                    // (which had Enabled=false and empty origins) with one that uses the
                    // test-specific allowed origin.
                    services.AddCors(options =>
                    {
                        options.AddPolicy(Startup.CorsPolicyName, policy =>
                        {
                            policy.WithOrigins(allowedOrigin)
                                  .WithMethods("GET", "HEAD", "OPTIONS")
                                  .WithHeaders("Content-Type", "Authorization", "If-None-Match", "If-Modified-Since")
                                  .DisallowCredentials();
                        });
                    });
                });
            });
    }

    private static void ReplaceDbContext(IServiceCollection services, SqliteConnection conn)
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
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Disabled path — byte-identical to no-CORS behavior
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CorsDisabled_GetWithOriginHeader_NoAccessControlHeaderEmitted()
    {
        // Even when the request carries an Origin header, the CORS middleware is not registered
        // and must not emit any Access-Control-* response headers.
        await using var factory = CreateDisabledFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "https://example.com");
        var response = await client.SendAsync(request);

        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Expected no Access-Control-Allow-Origin header when CORS is disabled.");
    }

    [Fact]
    public async Task CorsDisabled_PreflightOptionsWithOriginHeader_NoAccessControlHeaderEmitted()
    {
        // A preflight OPTIONS request must not receive CORS headers when Cors.Enabled=false.
        await using var factory = CreateDisabledFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        using var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", "https://example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);

        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Expected no Access-Control-Allow-Origin header for OPTIONS when CORS is disabled.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Enabled path — preflight + actual request
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CorsEnabled_GetWithMatchingOrigin_EmitsAccessControlAllowOriginHeader()
    {
        const string allowedOrigin = "https://ui.example.com";
        await using var factory = CreateEnabledFactory(allowedOrigin);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", allowedOrigin);
        var response = await client.SendAsync(request);

        Assert.True(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Expected Access-Control-Allow-Origin header on GET when CORS is enabled with matching origin.");
        Assert.Equal(allowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").First());
    }

    [Fact]
    public async Task CorsEnabled_PreflightWithMatchingOrigin_Succeeds()
    {
        // ASP.NET Core CORS middleware handles preflight by returning 204 with the allow headers.
        const string allowedOrigin = "https://ui.example.com";
        await using var factory = CreateEnabledFactory(allowedOrigin);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        using var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", allowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);

        // Preflight should succeed: 204 (or 200) + Access-Control-Allow-Origin.
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent ||
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 204 or 200 for preflight, got {(int)response.StatusCode}.");
        Assert.True(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Expected Access-Control-Allow-Origin header on preflight when CORS is enabled.");
        Assert.Equal(allowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").First());
    }

    [Fact]
    public async Task CorsEnabled_GetWithNonMatchingOrigin_NoAccessControlAllowOriginHeader()
    {
        // A request from a non-configured origin must not receive the ACAO header.
        const string allowedOrigin = "https://ui.example.com";
        await using var factory = CreateEnabledFactory(allowedOrigin);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "https://evil.example.com");
        var response = await client.SendAsync(request);

        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Expected no Access-Control-Allow-Origin header for a non-configured origin.");
    }
}
