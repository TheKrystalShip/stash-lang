using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Integration tests verifying that the bounded-domain enum deserializer rejects
/// illegal wire values at the HTTP boundary (returns 400 with ErrorResponse) rather
/// than propagating them into the database.
/// </summary>
/// <remarks>
/// P4 done_when: "POST /api/v1/admin/users with a body {"role":"owner"} (an illegal
/// UserRoles wire value) returns 400 with ErrorResponse — the deserializer rejects
/// at the boundary."
/// "owner" is a valid PackageRoles wire value but NOT a valid UserRoles wire value
/// (only "user" and "admin" are valid UserRoles).
/// </remarks>
public sealed class BoundedDomainBoundaryTests : RegistryAuthzTestBase
{
    /// <summary>
    /// POST /api/v1/admin/users with an invalid UserRoles value "owner" must return 400.
    /// This endpoint uses inline <c>JsonSerializer.DeserializeAsync</c> (not <c>[FromBody]</c>),
    /// so it exercises the manual deserialization path.
    /// </summary>
    [Fact]
    public async Task CreateUser_WithInvalidUserRole_Returns400()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // Register the first user (becomes admin) and get an admin-ceiling token.
        // Uses the existing test-base helper which properly sequences register → login → publish → admin.
        string adminToken = await RegisterAndGetAdminTokenAsync(client, "boundadmin");

        // Send a CREATE USER request with role="owner" — illegal for UserRoles
        // (only "user" and "admin" are valid UserRoles wire values).
        var requestBody = new { username = "testboundary", password = "password123", role = "owner" };
        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/users")
        {
            Content = content
        };
        SetBearer(client, adminToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        // Verify it's an ErrorResponse (has an "error" field)
        using var doc = JsonDocument.Parse(body);
        Assert.True(
            doc.RootElement.TryGetProperty("error", out _),
            $"Expected ErrorResponse with 'error' field, but got: {body}");
    }

    /// <summary>
    /// PUT /api/v1/packages/{scope}/{name}/roles with an illegal PackageRoles wire value must return
    /// 400 with an <c>ErrorResponse</c> body — NOT a <c>ValidationProblemDetails</c> body.
    /// This endpoint uses <c>[FromBody]</c> on an <c>[ApiController]</c>, so without the
    /// <c>InvalidModelStateResponseFactory</c> override the framework would return the RFC-7807
    /// shape that contradicts the published OpenAPI contract.
    /// The caller is seeded as the package owner so that authorization passes and the request
    /// actually reaches the model-binding step.
    /// </summary>
    [Fact]
    public async Task AssignRole_WithInvalidPackageRole_Returns400WithErrorResponse()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // alice-bd registers first and becomes the package owner.
        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-bd");
        await RegisterAndGetTokenAsync(client, "bob-bd");

        await SeedScopeAsync(factory, "alice-bd", "alice-bd");
        await SeedPackageAsync(factory, "@alice-bd/core", "alice-bd");

        // alice-bd (owner) sends an AssignRole request with an illegal PackageRoles value.
        // "NOT_A_REAL_ROLE" is not a member of PackageRoles so JsonStringEnumConverter will
        // reject it during model binding, triggering the InvalidModelStateResponseFactory.
        // principalType and principalId are valid — the sole failure is the illegal role value.
        SetBearer(client, aliceToken);
        var body = new { principalType = "user", principalId = "bob-bd", role = "NOT_A_REAL_ROLE" };
        var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await client.PutAsync("/api/v1/packages/alice-bd/core/roles", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);

        // Must be an ErrorResponse shape (has "error" field), NOT a ValidationProblemDetails
        // shape (which would have a "type" field with "https://tools.ietf.org/html/rfc7807").
        Assert.True(
            doc.RootElement.TryGetProperty("error", out _),
            $"Expected ErrorResponse with 'error' field, but got: {responseBody}");
        Assert.False(
            doc.RootElement.TryGetProperty("type", out _),
            $"Got ValidationProblemDetails instead of ErrorResponse: {responseBody}");
    }

    /// <summary>
    /// DELETE /api/v1/packages/{scope}/{name}/roles with an illegal PrincipalTypes wire value must
    /// return 400 with an <c>ErrorResponse</c> body — verifying the model-binding failure path is
    /// declared in the <c>RevokeRole</c> contract (the union now includes <c>BadRequest&lt;ErrorResponse&gt;</c>).
    /// </summary>
    [Fact]
    public async Task RevokeRole_WithInvalidPrincipalType_Returns400WithErrorResponse()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // alice-rv registers first and becomes the package owner.
        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-rv");

        await SeedScopeAsync(factory, "alice-rv", "alice-rv");
        await SeedPackageAsync(factory, "@alice-rv/core", "alice-rv");

        // alice-rv (owner) sends a RevokeRole request with an illegal PrincipalTypes value.
        // "NOT_A_REAL_TYPE" is not a member of PrincipalTypes so JsonStringEnumConverter will
        // reject it during model binding, triggering the InvalidModelStateResponseFactory.
        SetBearer(client, aliceToken);
        var body = new { principalType = "NOT_A_REAL_TYPE", principalId = "bob" };
        var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            "/api/v1/packages/alice-rv/core/roles") { Content = content });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string responseBody = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);

        // Must be an ErrorResponse shape (has "error" field), NOT a ValidationProblemDetails
        // shape (which would have a "type" field with "https://tools.ietf.org/html/rfc7807").
        Assert.True(
            doc.RootElement.TryGetProperty("error", out _),
            $"Expected ErrorResponse with 'error' field, but got: {responseBody}");
        Assert.False(
            doc.RootElement.TryGetProperty("type", out _),
            $"Got ValidationProblemDetails instead of ErrorResponse: {responseBody}");
    }
}
