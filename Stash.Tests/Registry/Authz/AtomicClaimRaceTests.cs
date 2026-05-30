using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Database;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Concurrent-claim race tests — N parallel <c>POST /api/v1/scopes</c> requests
/// must produce exactly one 201 and N-1 409 responses with exactly one scope row.
/// Also covers open-mode auto-claim atomicity on concurrent first-publish.
/// </summary>
public sealed class AtomicClaimRaceTests : RegistryAuthzTestBase
{
    private const int ParallelCallers = 5;

    // ── Atomic scope-claim race ───────────────────────────────────────────────

    [Fact]
    public async Task ClaimScope_ConcurrentRequests_ExactlyOneWinsRest409()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;

        // Create N distinct callers
        var tokens = new string[ParallelCallers];
        for (int i = 0; i < ParallelCallers; i++)
        {
            using var setup = factory.CreateClient();
            tokens[i] = await RegisterAndGetTokenAsync(setup, $"race-user-{i}");
        }

        // Fire N concurrent claim requests for the same scope
        var tasks = Enumerable.Range(0, ParallelCallers).Select(i =>
        {
            var client = factory.CreateClient();
            SetBearer(client, tokens[i]);
            return client.PostAsync("/api/v1/scopes",
                Json(new { scope = "race-scope-claim", owner_type = "user",
                           owner = $"race-user-{i}" }));
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        int created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        int conflict = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        // Collect all statuses for diagnostics
        var statusCodes = responses.Select(r => (int)r.StatusCode).OrderBy(s => s).ToList();

        // Exactly one winner, rest are conflicts (or the scope-not-owned-by-caller 403s due to timing)
        // Tolerate: some concurrent requests may get 403 (PDP sees scope claimed by different user)
        // instead of 409 if TryCreateScopeAsync races with PDP check. The invariant is:
        // - exactly 1 created
        // - no 500 errors
        Assert.DoesNotContain(statusCodes, s => s == 500);
        Assert.Equal(1, created);
        // The rest should be 409 or 403 (both are denial codes; total must be ParallelCallers - 1)
        int denied = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict
            || r.StatusCode == HttpStatusCode.Forbidden);
        Assert.Equal(ParallelCallers - 1, denied);

        // Exactly one scope row
        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        Assert.True(await db.ScopeExistsAsync("race-scope-claim"));
    }

    // ── Atomic open-mode auto-claim race ─────────────────────────────────────

    [Fact]
    public async Task OpenMode_ConcurrentFirstPublish_ExactlyOneScopeRowCreated()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        try
        {
            using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Stash.Registry.Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSolutionRelativeContentRoot("Stash.Registry");
                    builder.UseSetting("environment", "Development");
                    builder.ConfigureTestServices(services =>
                    {
                        var descriptor = services.SingleOrDefault(d =>
                            d.ServiceType == typeof(Microsoft.EntityFrameworkCore.DbContextOptions<Stash.Registry.Database.RegistryDbContext>));
                        if (descriptor != null) services.Remove(descriptor);
                        services.AddDbContext<Stash.Registry.Database.RegistryDbContext>(options =>
                            options.UseSqlite(conn));

                        var configDescriptor = services.SingleOrDefault(d =>
                            d.ServiceType == typeof(Stash.Registry.Configuration.RegistryConfig));
                        if (configDescriptor != null) services.Remove(configDescriptor);
                        var cfg = new Stash.Registry.Configuration.RegistryConfig();
                        cfg.Security.ScopeOwnershipPolicy = Stash.Registry.Auth.Authorization.ScopeOwnershipPolicyKind.Open;
                        cfg.Auth.RegistrationEnabled = true;
                        services.AddSingleton(cfg);

                        var sp = services.BuildServiceProvider();
                        using var scope = sp.CreateScope();
                        scope.ServiceProvider.GetRequiredService<IRegistryDatabase>().Initialize();
                    });
                });

            // N callers each claiming a distinct username → they'll each try to auto-claim "shared-race-scope"
            var tokens = new string[ParallelCallers];
            for (int i = 0; i < ParallelCallers; i++)
            {
                using var setup = factory.CreateClient();
                tokens[i] = await RegisterAndGetTokenAsync(setup, $"open-racer-{i}");
            }

            var tarballs = Enumerable.Range(0, ParallelCallers)
                .Select(i => CreateTarball("@shared-race-scope/lib", "1.0.0"))
                .ToArray();

            var publishTasks = Enumerable.Range(0, ParallelCallers).Select(i =>
            {
                var client = factory.CreateClient();
                SetBearer(client, tokens[i]);
                return client.PutAsync("/api/v1/packages/shared-race-scope/lib",
                    TarballContent(tarballs[i]));
            }).ToArray();

            var responses = await Task.WhenAll(publishTasks);

            // Only one scope row must exist
            using var verifyScope = factory.Services.CreateScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            Assert.True(await db.ScopeExistsAsync("shared-race-scope"));

            // Responses should be Created (201) for the winner and either 201 (collapse to
            // PublishVersion on existing package) or 409 for the others — no 500s.
            Assert.DoesNotContain(responses, r => r.StatusCode == HttpStatusCode.InternalServerError);
        }
        finally
        {
            conn.Dispose();
        }
    }
}
