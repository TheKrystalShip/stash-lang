using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Stash.Tests.Registry;

public class BasePathIntegrationTests
{
    private sealed class RegistryFactory : WebApplicationFactory<Stash.Registry.Program>
    {
        private readonly string _basePath;
        private readonly string _dbDir;

        public RegistryFactory(string basePath)
        {
            _basePath = basePath;
            _dbDir = Path.Combine(Path.GetTempPath(), $"stash-registry-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dbDir);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Development");
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Registry:Server:BasePath"] = _basePath,
                    ["Registry:Database:Type"] = "sqlite",
                    ["Registry:Database:Path"] = Path.Combine(_dbDir, "registry.db"),
                    ["Registry:Storage:Type"] = "filesystem",
                    ["Registry:Storage:Path"] = Path.Combine(_dbDir, "packages"),
                    ["Registry:RateLimiting:Enabled"] = "false",
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
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

        var rootRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/packages/nonexistent");
        var rootResponse = await client.SendAsync(rootRequest);
        Assert.Equal(HttpStatusCode.NotFound, rootResponse.StatusCode);

        var prefixedRequest = new HttpRequestMessage(HttpMethod.Get, "/registry/api/v1/packages/nonexistent");
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
