using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Stash.Cli.PackageManager;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Unit tests for the P2 parity methods added to <see cref="RegistryClient"/>.
/// Each test uses a capturing fake handler to assert the correct HTTP verb, URL,
/// and wire-key shape of the request body (snake_case keys matching the shared
/// <see cref="Stash.Registry.Contracts"/> types).
/// </summary>
[Collection("CliTests")]
public class RegistryClientParityTests
{
    private const string Base = "https://registry.example.com/api/v1";

    // ---------------------------------------------------------------------------
    // Capturing fake handler
    // ---------------------------------------------------------------------------

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public HttpMethod? CapturedMethod { get; private set; }
        public Uri? CapturedUri { get; private set; }
        public string? CapturedBody { get; private set; }

        public CapturingHandler(HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = "{}")
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedMethod = request.Method;
            CapturedUri = request.RequestUri;
            if (request.Content is not null)
            {
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private static RegistryClient BuildClient(CapturingHandler handler)
        => new RegistryClient(Base, new HttpClient(handler), token: "test-token");

    // ---------------------------------------------------------------------------
    // SetVisibility
    // ---------------------------------------------------------------------------

    [Fact]
    public void SetVisibility_SendsPatchToCorrectUrl_WithVisibilityBody()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"ok":true,"package":"alice/widget","visibility":"private"}""");
        var client = BuildClient(handler);

        bool result = client.SetVisibility("@alice/widget", "private");

        Assert.True(result);
        Assert.Equal("PATCH", handler.CapturedMethod!.Method);
        Assert.Equal($"{Base}/packages/alice/widget/visibility", handler.CapturedUri!.ToString());

        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        Assert.Equal("private", body.GetProperty("visibility").GetString());
    }

    [Fact]
    public void SetVisibility_ServerReturnsError_ReturnsFalse()
    {
        var handler = new CapturingHandler(HttpStatusCode.Forbidden, """{"error":"forbidden"}""");
        var client = BuildClient(handler);

        bool result = client.SetVisibility("@alice/widget", "private");

        Assert.False(result);
    }

    // ---------------------------------------------------------------------------
    // GetRoles
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetRoles_SendsGetToCorrectUrl_DeserializesResponse()
    {
        const string responseJson = """
            {"package":"alice/widget","roles":[{"principal_type":"user","principal_id":"alice","role":"owner"}]}
            """;
        var handler = new CapturingHandler(HttpStatusCode.OK, responseJson);
        var client = BuildClient(handler);

        PackageRolesListResponse? result = client.GetRoles("@alice/widget");

        Assert.NotNull(result);
        Assert.Equal(HttpMethod.Get, handler.CapturedMethod);
        Assert.Equal($"{Base}/packages/alice/widget/roles", handler.CapturedUri!.ToString());
        Assert.Single(result!.Roles);
        Assert.Equal("alice", result.Roles[0].PrincipalId);
        Assert.Equal("owner", result.Roles[0].Role);
    }

    [Fact]
    public void GetRoles_PackageNotFound_ReturnsNull()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound, """{"error":"not found"}""");
        var client = BuildClient(handler);

        PackageRolesListResponse? result = client.GetRoles("@alice/missing");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------------------
    // AssignRole
    // ---------------------------------------------------------------------------

    [Fact]
    public void AssignRole_SendsPutToCorrectUrl_WithPrincipalTypedBody()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var client = BuildClient(handler);

        bool result = client.AssignRole("@alice/widget", "team", "designers", "maintainer");

        Assert.True(result);
        Assert.Equal(HttpMethod.Put, handler.CapturedMethod);
        Assert.Equal($"{Base}/packages/alice/widget/roles", handler.CapturedUri!.ToString());

        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        // Snake_case wire keys are the load-bearing check
        Assert.Equal("team", body.GetProperty("principal_type").GetString());
        Assert.Equal("designers", body.GetProperty("principal_id").GetString());
        Assert.Equal("maintainer", body.GetProperty("role").GetString());
    }

    [Fact]
    public void AssignRole_UserPrincipal_SendsCorrectPrincipalType()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var client = BuildClient(handler);

        bool result = client.AssignRole("@alice/widget", "user", "bob", "publisher");

        Assert.True(result);
        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        Assert.Equal("user", body.GetProperty("principal_type").GetString());
        Assert.Equal("bob", body.GetProperty("principal_id").GetString());
        Assert.Equal("publisher", body.GetProperty("role").GetString());
    }

    // ---------------------------------------------------------------------------
    // RevokeRole
    // ---------------------------------------------------------------------------

    [Fact]
    public void RevokeRole_SendsDeleteToCorrectUrl_WithPrincipalTypedBody()
    {
        var handler = new CapturingHandler(HttpStatusCode.NoContent, "");
        var client = BuildClient(handler);

        bool result = client.RevokeRole("@alice/widget", "team", "designers");

        Assert.True(result);
        Assert.Equal(HttpMethod.Delete, handler.CapturedMethod);
        Assert.Equal($"{Base}/packages/alice/widget/roles", handler.CapturedUri!.ToString());

        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        Assert.Equal("team", body.GetProperty("principal_type").GetString());
        Assert.Equal("designers", body.GetProperty("principal_id").GetString());
    }

    [Fact]
    public void RevokeRole_Server404_ThrowsWithServerMessage()
    {
        const string errorBody = """{"error":"RoleNotFoundException","message":"principal holds no role on this package"}""";
        var handler = new CapturingHandler(HttpStatusCode.NotFound, errorBody);
        var client = BuildClient(handler);

        var ex = Assert.Throws<InvalidOperationException>(
            () => client.RevokeRole("@alice/widget", "user", "nobody"));

        // The exception carries the HTTP status name and the server's error body
        Assert.Contains("NotFound", ex.Message);
        Assert.Contains("RoleNotFoundException", ex.Message);
    }

    [Fact]
    public void RevokeRole_Server409LastOwner_ThrowsWithServerMessage()
    {
        const string errorBody = """{"error":"LastOwnerException","message":"cannot remove the last owner of a package"}""";
        var handler = new CapturingHandler(HttpStatusCode.Conflict, errorBody);
        var client = BuildClient(handler);

        var ex = Assert.Throws<InvalidOperationException>(
            () => client.RevokeRole("@alice/widget", "user", "alice"));

        // The exception carries the HTTP status name and the server's error body
        Assert.Contains("Conflict", ex.Message);
        Assert.Contains("LastOwnerException", ex.Message);
    }

    // ---------------------------------------------------------------------------
    // ClaimScope
    // ---------------------------------------------------------------------------

    [Fact]
    public void ClaimScope_SendsPostToScopes_WithOwnerBody()
    {
        const string responseJson = """{"scope":"acme","owner_type":"user","owner":"alice","state":"claimed"}""";
        var handler = new CapturingHandler(HttpStatusCode.Created, responseJson);
        var client = BuildClient(handler);

        ScopeDetailResponse? result = client.ClaimScope("acme", "user", "alice");

        Assert.NotNull(result);
        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Equal($"{Base}/scopes", handler.CapturedUri!.ToString());

        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        Assert.Equal("acme", body.GetProperty("scope").GetString());
        Assert.Equal("user", body.GetProperty("owner_type").GetString());
        Assert.Equal("alice", body.GetProperty("owner").GetString());

        Assert.Equal("acme", result!.Scope);
        Assert.Equal("claimed", result.State);
    }

    [Fact]
    public void ClaimScope_ServerError_ReturnsNull()
    {
        var handler = new CapturingHandler(HttpStatusCode.Conflict, """{"error":"scope already claimed"}""");
        var client = BuildClient(handler);

        ScopeDetailResponse? result = client.ClaimScope("acme", "user", "alice");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------------------
    // GetScope
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetScope_SendsGetToScopesPath_DeserializesResponse()
    {
        const string responseJson = """{"scope":"acme","owner_type":"org","owner":"acme-corp"}""";
        var handler = new CapturingHandler(HttpStatusCode.OK, responseJson);
        var client = BuildClient(handler);

        ScopeDetailResponse? result = client.GetScope("acme");

        Assert.NotNull(result);
        Assert.Equal(HttpMethod.Get, handler.CapturedMethod);
        Assert.Equal($"{Base}/scopes/acme", handler.CapturedUri!.ToString());
        Assert.Equal("acme", result!.Scope);
        Assert.Equal("org", result.OwnerType);
    }

    [Fact]
    public void GetScope_NotFound_ReturnsNull()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound);
        var client = BuildClient(handler);

        ScopeDetailResponse? result = client.GetScope("nonexistent");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------------------
    // CreateOrg
    // ---------------------------------------------------------------------------

    [Fact]
    public void CreateOrg_SendsPostToOrgs_WithNameAndDisplayName()
    {
        const string responseJson = """{"id":"org-1","name":"acme","display_name":"Acme Corp","created_at":"2026-01-01T00:00:00Z","created_by":"alice"}""";
        var handler = new CapturingHandler(HttpStatusCode.Created, responseJson);
        var client = BuildClient(handler);

        CreateOrgResponse? result = client.CreateOrg("acme", "Acme Corp");

        Assert.NotNull(result);
        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Equal($"{Base}/orgs", handler.CapturedUri!.ToString());

        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        Assert.Equal("acme", body.GetProperty("name").GetString());
        Assert.Equal("Acme Corp", body.GetProperty("display_name").GetString());

        Assert.Equal("acme", result!.Name);
        Assert.Equal("alice", result.CreatedBy);
    }

    [Fact]
    public void CreateOrg_NoDisplayName_OmitsDisplayNameFromBody()
    {
        const string responseJson = """{"id":"org-2","name":"acme2","created_at":"2026-01-01T00:00:00Z","created_by":"alice"}""";
        var handler = new CapturingHandler(HttpStatusCode.Created, responseJson);
        var client = BuildClient(handler);

        CreateOrgResponse? result = client.CreateOrg("acme2");

        Assert.NotNull(result);
        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        // display_name omitted when null (WhenWritingNull)
        Assert.False(body.TryGetProperty("display_name", out _));
    }

    [Fact]
    public void CreateOrg_ServerError_ReturnsNull()
    {
        var handler = new CapturingHandler(HttpStatusCode.Conflict, """{"error":"org name taken"}""");
        var client = BuildClient(handler);

        CreateOrgResponse? result = client.CreateOrg("acme");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------------------
    // GetOrg
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetOrg_SendsGetToOrgsPath_DeserializesResponse()
    {
        const string responseJson = """{"id":"org-1","name":"acme","display_name":"Acme Corp","created_at":"2026-01-01T00:00:00Z","created_by":"alice"}""";
        var handler = new CapturingHandler(HttpStatusCode.OK, responseJson);
        var client = BuildClient(handler);

        OrgDetailResponse? result = client.GetOrg("acme");

        Assert.NotNull(result);
        Assert.Equal(HttpMethod.Get, handler.CapturedMethod);
        Assert.Equal($"{Base}/orgs/acme", handler.CapturedUri!.ToString());
        Assert.Equal("acme", result!.Name);
        Assert.Equal("Acme Corp", result.DisplayName);
    }

    [Fact]
    public void GetOrg_NotFound_ReturnsNull()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound);
        var client = BuildClient(handler);

        OrgDetailResponse? result = client.GetOrg("nonexistent");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------------------
    // AddOrgMember
    // ---------------------------------------------------------------------------

    [Fact]
    public void AddOrgMember_SendsPostToOrgMembersPath_WithUsernameAndRole()
    {
        var handler = new CapturingHandler(HttpStatusCode.Created);
        var client = BuildClient(handler);

        bool result = client.AddOrgMember("acme", "bob", "member");

        Assert.True(result);
        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Equal($"{Base}/orgs/acme/members", handler.CapturedUri!.ToString());

        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        Assert.Equal("bob", body.GetProperty("username").GetString());
        Assert.Equal("member", body.GetProperty("org_role").GetString());
    }

    [Fact]
    public void AddOrgMember_NoRole_OmitsOrgRoleFromBody()
    {
        var handler = new CapturingHandler(HttpStatusCode.Created);
        var client = BuildClient(handler);

        bool result = client.AddOrgMember("acme", "bob");

        Assert.True(result);
        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        Assert.Equal("bob", body.GetProperty("username").GetString());
        // org_role omitted when null
        Assert.False(body.TryGetProperty("org_role", out _));
    }

    // ---------------------------------------------------------------------------
    // RemoveOrgMember
    // ---------------------------------------------------------------------------

    [Fact]
    public void RemoveOrgMember_SendsDeleteToOrgMemberPath()
    {
        var handler = new CapturingHandler(HttpStatusCode.NoContent, "");
        var client = BuildClient(handler);

        bool result = client.RemoveOrgMember("acme", "bob");

        Assert.True(result);
        Assert.Equal(HttpMethod.Delete, handler.CapturedMethod);
        Assert.Equal($"{Base}/orgs/acme/members/bob", handler.CapturedUri!.ToString());
    }

    [Fact]
    public void RemoveOrgMember_ServerError_ReturnsFalse()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound);
        var client = BuildClient(handler);

        bool result = client.RemoveOrgMember("acme", "nobody");

        Assert.False(result);
    }

    // ---------------------------------------------------------------------------
    // CreateTeam
    // ---------------------------------------------------------------------------

    [Fact]
    public void CreateTeam_SendsPostToOrgTeamsPath_WithTeamName()
    {
        const string responseJson = """{"id":"team-1","name":"designers","org_id":"org-1","created_at":"2026-01-01T00:00:00Z"}""";
        var handler = new CapturingHandler(HttpStatusCode.Created, responseJson);
        var client = BuildClient(handler);

        CreateTeamResponse? result = client.CreateTeam("acme", "designers");

        Assert.NotNull(result);
        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Equal($"{Base}/orgs/acme/teams", handler.CapturedUri!.ToString());

        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        Assert.Equal("designers", body.GetProperty("name").GetString());

        Assert.Equal("designers", result!.Name);
        Assert.Equal("org-1", result.OrgId);
    }

    [Fact]
    public void CreateTeam_ServerError_ReturnsNull()
    {
        var handler = new CapturingHandler(HttpStatusCode.Conflict, """{"error":"team name taken"}""");
        var client = BuildClient(handler);

        CreateTeamResponse? result = client.CreateTeam("acme", "designers");

        Assert.Null(result);
    }

    // ---------------------------------------------------------------------------
    // AddTeamMember
    // ---------------------------------------------------------------------------

    [Fact]
    public void AddTeamMember_SendsPostToTeamMembersPath_WithUsername()
    {
        var handler = new CapturingHandler(HttpStatusCode.Created);
        var client = BuildClient(handler);

        bool result = client.AddTeamMember("acme", "designers", "bob");

        Assert.True(result);
        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Equal($"{Base}/orgs/acme/teams/designers/members", handler.CapturedUri!.ToString());

        var body = JsonDocument.Parse(handler.CapturedBody!).RootElement;
        Assert.Equal("bob", body.GetProperty("username").GetString());
    }

    [Fact]
    public void AddTeamMember_ServerError_ReturnsFalse()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound);
        var client = BuildClient(handler);

        bool result = client.AddTeamMember("acme", "designers", "nobody");

        Assert.False(result);
    }
}
