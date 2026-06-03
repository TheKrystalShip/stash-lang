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
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Xunit;

namespace Stash.Tests.Registry.Validation;

/// <summary>
/// Integration tests verifying that <c>ScopesController.ClaimScope</c> rejects structurally
/// invalid requests via the declarative <c>ModelStateInvalidFilter</c> while still routing
/// well-formed requests through the inline PDP.
/// </summary>
/// <remarks>
/// <para>
/// ClaimScope carries <c>[ImperativeAuthz]</c> — the inline PDP call, the 409
/// <c>ScopeReserved</c>/<c>ScopeNotOwned</c> status mapping, and the cross-resource business
/// guards all stay in the action body. Only the JSON deserialize-and-empty-check ceremony is
/// replaced by <c>[FromBody] ClaimScopeRequest</c> + DataAnnotations + <c>IValidatableObject</c>.
/// </para>
/// <para>
/// Each test spins up a full in-process ASP.NET Core server backed by an in-memory SQLite
/// database.
/// </para>
/// </remarks>
public sealed class ScopesControllerValidationTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private static WebApplicationFactory<Stash.Registry.Program> CreateFactory(SqliteConnection conn)
    {
        return new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
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

    private static StringContent Json(object body)
        => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<ErrorResponse?> ReadErrorResponseAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        try
        {
            return JsonSerializer.Deserialize<ErrorResponse>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Registers a user and issues a publish-ceiling bearer token.
    /// </summary>
    private static async Task<string> RegisterAndGetPublishTokenAsync(
        HttpClient client,
        string username)
    {
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username, password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username, password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        string loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string loginToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        var savedAuth = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginToken);
        var tokenResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "1d" }));
        client.DefaultRequestHeaders.Authorization = savedAuth;

        tokenResp.EnsureSuccessStatusCode();
        string tokenBody = await tokenResp.Content.ReadAsStringAsync();
        using var tokenDoc = JsonDocument.Parse(tokenBody);
        return tokenDoc.RootElement.GetProperty("token").GetString()!;
    }

    // ── Missing owner_type returns 400 ────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/scopes with a missing <c>owner_type</c> field returns 400
    /// <c>InvalidRequest</c>. The inline PDP is never entered (no scope is claimed).
    /// <para>
    /// Previously the action body had an explicit null check on <c>ownerTypeRaw</c>; after
    /// migration to <c>[FromBody] ClaimScopeRequest</c> with <c>IValidatableObject</c>, the
    /// <c>ModelStateInvalidFilter</c> rejects the request before the action runs.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ClaimScope_MissingOwnerType_Returns400InvalidRequest()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string token = await RegisterAndGetPublishTokenAsync(client, "scope-cs-notype");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Send a body with a valid scope and owner but NO owner_type.
        var response = await client.PostAsync("/api/v1/scopes",
            Json(new { scope = "mytest-scope", owner = "scope-cs-notype" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
    }

    // ── Valid body reaches the inline PDP ────────────────────────────────────

    /// <summary>
    /// POST /api/v1/scopes with a well-formed body reaches the inline PDP. For a user
    /// claiming a scope that is not taken by any user or org, the PDP should allow it
    /// and the scope is created (201). This confirms that the model-binding migration is
    /// wire-transparent for valid requests.
    /// </summary>
    [Fact]
    public async Task ClaimScope_ValidBody_ReachesPdpAndAllows()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        // Use a username that does NOT match the scope name to avoid the
        // namespace-pool collision check (GetUserAsync(scopeName) != null → 409).
        string username = "scope-claimowner";
        string scopeName = "myscope-valid";
        string token = await RegisterAndGetPublishTokenAsync(client, username);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Claim a user scope for the authenticated user (scope name differs from username).
        var response = await client.PostAsync("/api/v1/scopes",
            Json(new { scope = scopeName, owner_type = "user", owner = username }));

        // The PDP should allow this (caller claims scope on their own behalf).
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
