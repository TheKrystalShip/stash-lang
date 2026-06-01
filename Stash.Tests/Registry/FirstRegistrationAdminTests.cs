using System;
using System.Collections.Generic;
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
using Xunit;

namespace Stash.Tests.Registry;

public sealed class FirstRegistrationAdminTests : IDisposable
{
    // Shared in-memory SQLite connection so each factory test has a fresh DB.
    // Note: a new connection (and factory) is created per test via CreateFactory().
    private SqliteConnection? _connection;

    public void Dispose()
    {
        _connection?.Dispose();
    }

    private WebApplicationFactory<Stash.Registry.Program> CreateFactory()
    {
        // Each call creates a fresh in-memory SQLite connection. The connection is kept
        // open for the lifetime of the factory so EF Core can use it.
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
                    // Remove the real DbContext registration.
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(DbContextOptions<RegistryDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    // Re-register with the shared in-memory connection.
                    services.AddDbContext<RegistryDbContext>(options =>
                        options.UseSqlite(conn));

                    // Ensure schema exists.
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
    public async Task FirstRegistration_BecomesAdmin()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "firstuser", password = "password123" }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        var user = await db.GetUserAsync("firstuser");
        Assert.NotNull(user);
        Assert.Equal("admin", user!.Role);
    }

    [Fact]
    public async Task SecondRegistration_IsUser()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "firstuser", password = "password123" }));
        var response2 = await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "seconduser", password = "password123" }));
        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();

        var first = await db.GetUserAsync("firstuser");
        var second = await db.GetUserAsync("seconduser");
        Assert.Equal("admin", first!.Role);
        Assert.Equal("user", second!.Role);
    }

    [Fact]
    public async Task PreSeededAdmin_NewRegistration_IsUser()
    {
        await using var factory = CreateFactory();

        // Pre-seed an admin directly via DB before the HTTP request.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            string hash = LocalAuthProvider.HashPasswordInternal("adminpass");
            await db.CreateUserAsync("preseedadmin", hash, "admin");
        }

        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "lateuser", password = "password123" }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        var lateUser = await db2.GetUserAsync("lateuser");
        Assert.Equal("user", lateUser!.Role);
    }
}

