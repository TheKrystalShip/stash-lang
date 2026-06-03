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
}
