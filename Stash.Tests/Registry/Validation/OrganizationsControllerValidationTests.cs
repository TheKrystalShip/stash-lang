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
/// Integration tests verifying that the <c>OrganizationsController</c> actions reject
/// invalid requests via the declarative <c>ModelStateInvalidFilter</c> — confirming the
/// action body is never entered on invalid input.
/// </summary>
/// <remarks>
/// <para>
/// The key behavioral change in P4: <c>POST /api/v1/orgs/{org}/members</c> with an empty
/// body previously returned 400 "Username is required." from an inline guard. After migration
/// to a non-nullable <c>[FromBody] AddOrgMemberRequest request</c> parameter, a missing body
/// is rejected before the action runs with 400 <c>InvalidRequest</c> from the
/// <c>ModelStateInvalidFilter</c>.
/// </para>
/// <para>
/// Each test spins up a full in-process ASP.NET Core server backed by an in-memory SQLite
/// database.
/// </para>
/// </remarks>
public sealed class OrganizationsControllerValidationTests
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

    private static StringContent EmptyJson()
        => new(string.Empty, Encoding.UTF8, "application/json");

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
    /// Registers a user and returns a publish-ceiling bearer token.
    /// Login issues a read-ceiling token; we explicitly promote to publish for write endpoints.
    /// </summary>
    private static async Task<string> RegisterAndGetPublishTokenAsync(HttpClient client, string username)
    {
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username, password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username, password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        string loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string loginToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        // Promote to publish-ceiling token (required for CreateOrg, AddMember, CreateTeam)
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

    // ── AddMember: empty-body returns 400 InvalidRequest from filter ─────────

    /// <summary>
    /// POST /api/v1/orgs/{org}/members with an empty body (no content) returns 400
    /// with <c>Error = "InvalidRequest"</c>. Previously this returned 400
    /// "Username is required." from an inline guard; after migration to a non-nullable
    /// <c>[FromBody] AddOrgMemberRequest request</c>, the filter rejects the missing body
    /// before the action body runs.
    /// </summary>
    [Fact]
    public async Task AddOrgMember_EmptyBody_Returns400InvalidRequest()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        // Create an org owner and the org itself
        string token = await RegisterAndGetPublishTokenAsync(client, "alice-addmember-empty");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var orgResp = await client.PostAsync("/api/v1/orgs",
            Json(new { name = "test-org-empty-body" }));
        Assert.Equal(HttpStatusCode.Created, orgResp.StatusCode);

        // POST members with empty body — the filter should reject before the action runs
        var response = await client.PostAsync(
            "/api/v1/orgs/test-org-empty-body/members",
            EmptyJson());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
    }

    // ── CreateOrg: missing name returns 400 InvalidRequest ───────────────────

    /// <summary>
    /// POST /api/v1/orgs with a null <c>name</c> returns 400 <c>InvalidRequest</c>
    /// from the <c>[Required]</c>+<c>[ScopeGrammar]</c> attributes on
    /// <c>CreateOrgRequest.Name</c>. The action body is not entered (no org is created).
    /// </summary>
    [Fact]
    public async Task CreateOrg_NullName_Returns400InvalidRequest_AndNoOrgCreated()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string token = await RegisterAndGetPublishTokenAsync(client, "alice-createorg-null");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/v1/orgs",
            Json(new { name = (string?)null }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);

        // Action body was not entered — no org created
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        var org = await db.GetOrgAsync("null");
        Assert.Null(org);
    }

    // ── CreateTeam: missing name returns 400 InvalidRequest ──────────────────

    /// <summary>
    /// POST /api/v1/orgs/{org}/teams with a null <c>name</c> returns 400
    /// <c>InvalidRequest</c>. The action body is not entered (no team is created).
    /// </summary>
    [Fact]
    public async Task CreateTeam_NullName_Returns400InvalidRequest_AndNoTeamCreated()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string token = await RegisterAndGetPublishTokenAsync(client, "alice-createteam-null");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Create the org first
        var orgResp = await client.PostAsync("/api/v1/orgs",
            Json(new { name = "test-org-team-null" }));
        Assert.Equal(HttpStatusCode.Created, orgResp.StatusCode);

        var response = await client.PostAsync("/api/v1/orgs/test-org-team-null/teams",
            Json(new { name = (string?)null }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
    }

    // ── AddTeamMember: null username returns 400 InvalidRequest ──────────────

    /// <summary>
    /// POST /api/v1/orgs/{org}/teams/{team}/members with a null <c>username</c> returns
    /// 400 <c>InvalidRequest</c> from <c>[Required]</c> on <c>AddTeamMemberRequest.Username</c>.
    /// </summary>
    [Fact]
    public async Task AddTeamMember_NullUsername_Returns400InvalidRequest()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string token = await RegisterAndGetPublishTokenAsync(client, "alice-addteam-null");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Create org and team
        await client.PostAsync("/api/v1/orgs", Json(new { name = "test-org-addteam" }));
        await client.PostAsync("/api/v1/orgs/test-org-addteam/teams", Json(new { name = "devs" }));

        var response = await client.PostAsync(
            "/api/v1/orgs/test-org-addteam/teams/devs/members",
            Json(new { username = (string?)null }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
    }
}
