using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Database;
using Xunit;

namespace Stash.Tests.Registry;

public class BasePathIntegrationTests
{
    private sealed class RegistryFactory : WebApplicationFactory<Stash.Registry.Program>
    {
        private readonly string _basePath;
        private readonly string _dbDir;
        // Keep a single in-memory SQLite connection open for the factory's lifetime so the
        // schema we create at service-config time persists across DI scopes. The working
        // registry fixtures (AuthEndpointTests, FirstRegistrationAdminTests) use the same
        // shared-connection pattern; relying on the host's own startup Initialize() (which
        // reads _config built before the test's in-memory config lands) seeded system scopes
        // before the schema existed → "no such table: scopes".
        private readonly SqliteConnection _connection;

        public RegistryFactory(string basePath)
        {
            _basePath = basePath;
            _dbDir = Path.Combine(Path.GetTempPath(), $"stash-registry-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dbDir);
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Pin content root to an absolute path so the factory is cwd-independent
            // (parallel cwd-mutating tests would otherwise break relative resolution).
            builder.UseSolutionRelativeContentRoot("Stash.Registry");
            builder.UseSetting("environment", "Development");
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Registry:Server:BasePath"] = _basePath,
                    ["Registry:Database:Type"] = "sqlite",
                    ["Registry:Storage:Type"] = "filesystem",
                    ["Registry:Storage:Path"] = Path.Combine(_dbDir, "packages"),
                    ["Registry:RateLimiting:Enabled"] = "false",
                });
            });
            builder.ConfigureTestServices(services =>
            {
                // Replace the host's DbContext registration with the shared in-memory
                // connection, then create the schema before the host's startup seeding runs.
                var descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<RegistryDbContext>));
                if (descriptor != null)
                    services.Remove(descriptor);

                services.AddDbContext<RegistryDbContext>(options =>
                    options.UseSqlite(_connection));

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                db.Initialize();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _connection.Dispose();
            if (Directory.Exists(_dbDir))
            {
                try { Directory.Delete(_dbDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task BasePath_Unset_HealthAtRoot()
    {
        await using var factory = new RegistryFactory("");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BasePath_Set_HealthAtPrefix()
    {
        await using var factory = new RegistryFactory("/registry");
        using var client = factory.CreateClient();

        var prefixed = await client.GetAsync("/registry");
        Assert.Equal(HttpStatusCode.OK, prefixed.StatusCode);

        // Use a fully scoped path ({scope}/{name}) so it matches the PackagesController route;
        // a single-segment path would never match and would return an empty unmatched-route body.
        var rootRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/packages/myscope/nonexistent");
        var rootResponse = await client.SendAsync(rootRequest);
        Assert.Equal(HttpStatusCode.NotFound, rootResponse.StatusCode);

        var prefixedRequest = new HttpRequestMessage(HttpMethod.Get, "/registry/api/v1/packages/myscope/nonexistent");
        var prefixedResponse = await client.SendAsync(prefixedRequest);
        // Either 404 (package not found) or 200 — the key assertion is that the route MATCHED, not 404 from routing miss.
        // PackagesController returns 404 from the action body when the package is not found, so we just verify
        // that we get a JSON-shaped response rather than the empty body of an unmatched route.
        Assert.True(prefixedResponse.StatusCode == HttpStatusCode.NotFound || prefixedResponse.StatusCode == HttpStatusCode.OK);
        var body = await prefixedResponse.Content.ReadAsStringAsync();
        // Successful match emits a JSON error body; unmatched route emits empty.
        Assert.False(string.IsNullOrEmpty(body), "Expected route to match and return a body");
    }
}
