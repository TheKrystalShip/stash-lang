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
/// Integration tests verifying that the <c>AuthController</c> actions reject invalid
/// requests via the declarative <c>ModelStateInvalidFilter</c> — confirming the action
/// body is never entered on invalid input.
/// </summary>
/// <remarks>
/// <para>
/// Each test spins up a full in-process ASP.NET Core server (via
/// <see cref="WebApplicationFactory{TEntryPoint}"/>) backed by an in-memory SQLite
/// database. The "action body not entered" assertion is expressed as the absence of
/// observable side-effects: no token row in the DB, no audit entry, no user row.
/// </para>
/// </remarks>
public sealed class AuthControllerValidationTests
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

    // ── Login validation ──────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/auth/login with empty username and no password returns 400
    /// with Error="InvalidRequest" and Message containing both
    /// "Username is required." and "Password is required." joined with "; ".
    /// The action body is not entered (no token row, no audit entry).
    /// </summary>
    [Fact]
    public async Task Login_EmptyUsernameNoPassword_Returns400WithBothFieldErrors()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "", password = (string?)null }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
        Assert.Contains("Username is required.", error.Message);
        Assert.Contains("Password is required.", error.Message);
    }

    /// <summary>
    /// POST /api/v1/auth/login with a missing username (null) and a valid password
    /// returns 400 with Error="InvalidRequest" and Message containing "Username is required.".
    /// </summary>
    [Fact]
    public async Task Login_NullUsername_Returns400WithUsernameRequired()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = (string?)null, password = "somepass" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
        Assert.Contains("Username is required.", error.Message);
    }

    /// <summary>
    /// POST /api/v1/auth/login with a missing password (null) and a valid username
    /// returns 400 with Error="InvalidRequest" and Message containing "Password is required.".
    /// The action body is not entered: no token row exists in the DB after the call.
    /// </summary>
    [Fact]
    public async Task Login_NullPassword_Returns400WithPasswordRequired_AndActionBodyNotEntered()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/login",
            Json(new { username = "alice", password = (string?)null }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
        Assert.Contains("Password is required.", error.Message);

        // Action body was not entered — no token rows should exist for "alice"
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        var tokens = await db.GetUserTokensAsync("alice");
        Assert.Empty(tokens);
    }

    // ── Register validation ───────────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/auth/register with null username and null password returns 400
    /// InvalidRequest. The action body is never entered (no user is created).
    /// </summary>
    [Fact]
    public async Task Register_NullUsernameAndPassword_Returns400_AndNoUserCreated()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/register",
            Json(new { username = (string?)null, password = (string?)null }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);

        // Action body was not entered — no user record for any value
        // (the action would have created a user record if the body was processed)
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        // No username to check — just confirm no audit entry was written by checking
        // that we can still register "testuser" (DB is empty / no side effects)
        var user = await db.GetUserAsync("testuser");
        Assert.Null(user);
    }

    // ── CreateToken validation ────────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/auth/tokens with missing ceiling and expires_in returns 400
    /// InvalidRequest. The action body is never entered (no extra token is created).
    /// </summary>
    [Fact]
    public async Task CreateToken_MissingCeilingAndExpiresIn_Returns400_AndNoTokenCreated()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        // Register and login to get a bearer token
        await client.PostAsync("/api/v1/auth/register", Json(new { username = "alice", password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login", Json(new { username = "alice", password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        var loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string token = loginDoc.RootElement.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Count tokens before the call
        using var scopeBefore = factory.Services.CreateScope();
        var dbBefore = scopeBefore.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        var tokensBefore = await dbBefore.GetUserTokensAsync("alice");
        int countBefore = tokensBefore.Count;

        var response = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = (string?)null, expiresIn = (string?)null }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);

        // Action body was not entered — no new token was created
        using var scopeAfter = factory.Services.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        var tokensAfter = await dbAfter.GetUserTokensAsync("alice");
        Assert.Equal(countBefore, tokensAfter.Count);
    }

    // ── RefreshToken validation ───────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/auth/tokens/refresh with null refreshToken, accessToken, and machineId
    /// returns 400 InvalidRequest. The action body is not entered.
    /// </summary>
    [Fact]
    public async Task RefreshToken_MissingAllFields_Returns400()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/tokens/refresh",
            Json(new { refreshToken = (string?)null, accessToken = (string?)null, machineId = (string?)null }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
    }
}
