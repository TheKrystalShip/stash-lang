using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Tests for atomic package creation using insert-then-handle-unique-violation (D20).
/// </summary>
/// <remarks>
/// Joins the serial <c>RegistryConcurrency</c> collection: this class fires N concurrent
/// publish requests and asserts zero 500s, which is unreliable when other registry test
/// classes contend on the shared SQLite lock in parallel. See
/// <see cref="RegistryConcurrencyCollection"/>.
/// </remarks>
[Collection("RegistryConcurrency")]
public sealed class RegistryAuthzAtomicCreateTests : RegistryAuthzTestBase
{
    // ── Schema constraint ────────────────────────────────────────────────────

    [Fact]
    public void Schema_PackageName_IsUniqueConstraint()
    {
        // Inserts two rows with the same name and asserts a constraint violation.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(conn)
            .Options;
        using var ctx = new RegistryDbContext(options);
        var db = new StashRegistryDatabase(ctx);
        db.Initialize();

        var pkg1 = new PackageRecord
        {
            Name = "@test/duplicate",
            Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ctx.Packages.Add(pkg1);
        ctx.SaveChanges();

        // Detach the tracked entity so EF doesn't throw before hitting the DB
        ctx.Entry(pkg1).State = Microsoft.EntityFrameworkCore.EntityState.Detached;

        var pkg2 = new PackageRecord
        {
            Name = "@test/duplicate",
            Latest = "2.0.0",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ctx.Packages.Add(pkg2);

        var ex = Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
        Assert.NotNull(ex.InnerException);
        Assert.True(
            ex.InnerException is SqliteException { SqliteErrorCode: 19 } ||
            (ex.InnerException?.Message?.Contains("UNIQUE constraint failed") ?? false),
            $"Expected unique constraint violation but got: {ex.InnerException?.GetType()?.Name}: {ex.InnerException?.Message}");
    }

    // ── Concurrent create ─────────────────────────────────────────────────────

    [Fact]
    public async Task AtomicCreate_ConcurrentFirstPublish_ExactlyOnePackageRow_ZeroFiveHundreds()
    {
        const int parallelism = 5;

        // Concurrent test: shared-cache in-memory DB so each request opens its own
        // connection (see RegistryConcurrencyCollection / CreateConcurrent).
        await using var ctx = RegistryAuthzFactory.CreateConcurrent();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-ac");
        await SeedScopeAsync(factory, "alice-ac", "alice-ac");

        var tasks = new List<Task<System.Net.Http.HttpResponseMessage>>();
        for (int i = 0; i < parallelism; i++)
        {
            int idx = i;
            tasks.Add(Task.Run(async () =>
            {
                using var c = factory.CreateClient();
                c.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                byte[] tarball = CreateTarball("@alice-ac/concurrent", $"1.0.{idx}");
                return await c.PutAsync(
                    "/api/v1/packages/alice-ac/concurrent",
                    TarballContent(tarball));
            }));
        }

        var responses = await Task.WhenAll(tasks);

        // Zero 500s
        foreach (var r in responses)
        {
            Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode);
        }

        // Package should exist
        Assert.True(await PackageExistsAsync(factory, "@alice-ac/concurrent"));

        int created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        int conflicts = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        int forbidden = responses.Count(r => r.StatusCode == HttpStatusCode.Forbidden);

        Assert.Equal(parallelism, created + conflicts + forbidden);
    }

    // ── Simple integration round-trip ─────────────────────────────────────────

    [Fact]
    public async Task AtomicCreate_FirstPublish_CreatesPackageAndOwnerRole()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-ac2");
        await SeedScopeAsync(factory, "alice-ac2", "alice-ac2");
        SetBearer(client, token);

        byte[] tarball = CreateTarball("@alice-ac2/new-pkg", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-ac2/new-pkg", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.True(await PackageExistsAsync(factory, "@alice-ac2/new-pkg"));

        var roles = await GetPackageRolesAsync(factory, "@alice-ac2/new-pkg");
        Assert.Contains(roles, r => r.PrincipalId == "alice-ac2" && r.Role == "owner");
    }

    [Fact]
    public async Task AtomicCreate_SecondPublishSamePackage_IsPublishVersion()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-ac3");
        await SeedScopeAsync(factory, "alice-ac3", "alice-ac3");
        SetBearer(client, token);

        byte[] tarball1 = CreateTarball("@alice-ac3/lib", "1.0.0");
        var resp1 = await client.PutAsync("/api/v1/packages/alice-ac3/lib", TarballContent(tarball1));
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        byte[] tarball2 = CreateTarball("@alice-ac3/lib", "2.0.0");
        var resp2 = await client.PutAsync("/api/v1/packages/alice-ac3/lib", TarballContent(tarball2));
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        Assert.True(await db.VersionExistsAsync("@alice-ac3/lib", "1.0.0"));
        Assert.True(await db.VersionExistsAsync("@alice-ac3/lib", "2.0.0"));
    }
}
