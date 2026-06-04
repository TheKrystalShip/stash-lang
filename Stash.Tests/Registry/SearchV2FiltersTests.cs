using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
using Stash.Registry.Database.Models;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Integration tests for the Search v2 column-backed filters (Bucket-A only):
/// <c>keyword</c>, <c>license</c>, <c>deprecated</c>, <c>owner</c>.
/// Also verifies that <see cref="PackageSummaryResponse"/> now includes
/// <c>license</c> and <c>ownerCount</c> fields.
/// </summary>
public sealed class SearchV2FiltersTests
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

    /// <summary>Seeds a package record directly into the DB via the scoped IRegistryDatabase.</summary>
    private static async Task SeedPackageAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        PackageRecord package)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        await db.CreatePackageAsync(package);
    }

    /// <summary>Seeds a user (for the owner filter tests).</summary>
    private static async Task SeedUserAndRoleAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        string packageName,
        string username)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        await db.CreateUserAsync(username, "hash", "user");
        await db.AssignPackageRoleAsync(packageName, "user", username, "owner");
    }

    private static PackageRecord MakePackage(string name) => new PackageRecord
    {
        Name = name,
        Latest = "1.0.0",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Visibility = Visibilities.Public,
    };

    // ── license field in PackageSummaryResponse ───────────────────────────────

    /// <summary>
    /// PackageSummaryResponse now includes a <c>license</c> field populated from
    /// <c>PackageRecord.License</c>. This test asserts the field is present and correct.
    /// </summary>
    [Fact]
    public async Task Search_Response_IncludesLicenseField()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var pkg = MakePackage("license-pkg");
        pkg.License = "MIT";
        await SeedPackageAsync(factory, pkg);

        var response = await client.GetAsync("/api/v1/search?q=license-pkg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var item = items[0];

        Assert.True(item.TryGetProperty("license", out var licProp),
            "PackageSummaryResponse must include a 'license' field.");
        Assert.Equal("MIT", licProp.GetString());
    }

    /// <summary>
    /// When a package has no license set, the <c>license</c> field must be null (not absent).
    /// </summary>
    [Fact]
    public async Task Search_Response_LicenseIsNullWhenNotSet()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        await SeedPackageAsync(factory, MakePackage("no-license-pkg"));

        var response = await client.GetAsync("/api/v1/search?q=no-license-pkg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());

        Assert.True(items[0].TryGetProperty("license", out var licProp),
            "PackageSummaryResponse must include a 'license' field.");
        Assert.Equal(JsonValueKind.Null, licProp.ValueKind);
    }

    // ── ownerCount field in PackageSummaryResponse ────────────────────────────

    /// <summary>
    /// PackageSummaryResponse now includes an <c>ownerCount</c> field reflecting the count
    /// of user principals with Owner role on the package. No role → ownerCount = 0.
    /// </summary>
    [Fact]
    public async Task Search_Response_OwnerCountIsZeroWhenNoOwnerRole()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        await SeedPackageAsync(factory, MakePackage("no-owner-pkg"));

        var response = await client.GetAsync("/api/v1/search?q=no-owner-pkg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());

        Assert.True(items[0].TryGetProperty("ownerCount", out var ownerCountProp),
            "PackageSummaryResponse must include an 'ownerCount' field.");
        Assert.Equal(0, ownerCountProp.GetInt32());
    }

    /// <summary>
    /// PackageSummaryResponse <c>ownerCount</c> counts only user principals with Owner role.
    /// After assigning owner role to one user, ownerCount must equal 1.
    /// </summary>
    [Fact]
    public async Task Search_Response_OwnerCountReflectsUserOwners()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        await SeedPackageAsync(factory, MakePackage("owned-pkg"));
        await SeedUserAndRoleAsync(factory, "owned-pkg", "alice");

        var response = await client.GetAsync("/api/v1/search?q=owned-pkg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());

        var ownerCount = items[0].GetProperty("ownerCount").GetInt32();
        Assert.Equal(1, ownerCount);
    }

    // ── license filter ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>license=MIT</c> returns only packages with <c>PackageRecord.License == "MIT"</c>.
    /// </summary>
    [Fact]
    public async Task Search_LicenseFilter_ReturnsOnlyMatchingPackages()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var mit = MakePackage("mit-pkg");
        mit.License = "MIT";
        await SeedPackageAsync(factory, mit);

        var apache = MakePackage("apache-pkg");
        apache.License = "Apache-2.0";
        await SeedPackageAsync(factory, apache);

        await SeedPackageAsync(factory, MakePackage("no-license-pkg2"));

        var response = await client.GetAsync("/api/v1/search?license=MIT");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("mit-pkg", items[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// <c>license=NonExistent</c> returns zero packages (not an error).
    /// </summary>
    [Fact]
    public async Task Search_LicenseFilter_NoMatch_ReturnsEmpty()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var mit = MakePackage("only-mit");
        mit.License = "MIT";
        await SeedPackageAsync(factory, mit);

        var response = await client.GetAsync("/api/v1/search?license=GPL-3.0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        Assert.Equal(0, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    // ── keyword filter ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>keyword=foo</c> returns only packages whose keywords array contains exactly <c>"foo"</c>.
    /// </summary>
    [Fact]
    public async Task Search_KeywordFilter_ReturnsOnlyMatchingPackages()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var withFoo = MakePackage("has-foo-kw");
        withFoo.Keywords = "[\"foo\",\"bar\"]";
        await SeedPackageAsync(factory, withFoo);

        var withBar = MakePackage("has-bar-kw");
        withBar.Keywords = "[\"bar\"]";
        await SeedPackageAsync(factory, withBar);

        await SeedPackageAsync(factory, MakePackage("no-kw-pkg2"));

        var response = await client.GetAsync("/api/v1/search?keyword=foo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("has-foo-kw", items[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// The keyword filter does not perform partial-word matching: <c>keyword=fo</c>
    /// must NOT match a package whose only keyword is <c>"foo"</c>.
    /// </summary>
    [Fact]
    public async Task Search_KeywordFilter_DoesNotMatchPartialKeyword()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var withFoo = MakePackage("exact-kw-pkg");
        withFoo.Keywords = "[\"foo\"]";
        await SeedPackageAsync(factory, withFoo);

        var response = await client.GetAsync("/api/v1/search?keyword=fo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        Assert.Equal(0, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    // ── deprecated filter ─────────────────────────────────────────────────────

    /// <summary>
    /// <c>deprecated=true</c> returns only deprecated packages.
    /// </summary>
    [Fact]
    public async Task Search_DeprecatedFilter_True_ReturnsOnlyDeprecated()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var dep = MakePackage("deprecated-pkg");
        dep.Deprecated = true;
        await SeedPackageAsync(factory, dep);

        await SeedPackageAsync(factory, MakePackage("active-pkg"));

        var response = await client.GetAsync("/api/v1/search?deprecated=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("deprecated-pkg", items[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// <c>deprecated=false</c> returns only non-deprecated packages.
    /// </summary>
    [Fact]
    public async Task Search_DeprecatedFilter_False_ReturnsOnlyActive()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var dep = MakePackage("deprecated-pkg2");
        dep.Deprecated = true;
        await SeedPackageAsync(factory, dep);

        await SeedPackageAsync(factory, MakePackage("active-pkg2"));

        var response = await client.GetAsync("/api/v1/search?deprecated=false");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("active-pkg2", items[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// Omitting <c>deprecated</c> returns both deprecated and active packages.
    /// </summary>
    [Fact]
    public async Task Search_DeprecatedFilter_Absent_ReturnsBoth()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var dep = MakePackage("dep-mixed");
        dep.Deprecated = true;
        await SeedPackageAsync(factory, dep);

        await SeedPackageAsync(factory, MakePackage("active-mixed"));

        var response = await client.GetAsync("/api/v1/search");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        Assert.Equal(2, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    // ── owner filter ──────────────────────────────────────────────────────────

    /// <summary>
    /// <c>owner=alice</c> returns only packages where alice is a user principal with Owner role.
    /// </summary>
    [Fact]
    public async Task Search_OwnerFilter_ReturnsOnlyPackagesOwnedByUser()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        await SeedPackageAsync(factory, MakePackage("alice-owns-this"));
        await SeedUserAndRoleAsync(factory, "alice-owns-this", "alice");

        await SeedPackageAsync(factory, MakePackage("bob-owns-this"));
        // bob gets owner via SeedUserAndRoleAsync — creates user + assigns owner
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            await db.CreateUserAsync("bob", "hash", "user");
            await db.AssignPackageRoleAsync("bob-owns-this", "user", "bob", "owner");
        }

        await SeedPackageAsync(factory, MakePackage("nobody-owns-this"));

        var response = await client.GetAsync("/api/v1/search?owner=alice");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("alice-owns-this", items[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// The owner filter is restricted to <c>Role=Owner</c> — a user with only
    /// <c>reader</c> role is NOT matched by the owner filter.
    /// </summary>
    [Fact]
    public async Task Search_OwnerFilter_ReaderRoleNotMatched()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        await SeedPackageAsync(factory, MakePackage("reader-role-pkg"));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            await db.CreateUserAsync("carol", "hash", "user");
            await db.AssignPackageRoleAsync("reader-role-pkg", "user", "carol", "reader");
        }

        var response = await client.GetAsync("/api/v1/search?owner=carol");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        Assert.Equal(0, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    // ── Bucket-B values not accepted ──────────────────────────────────────────

    /// <summary>
    /// <c>sort=downloads</c> is a Bucket-B value not in <see cref="PackageSortOrder"/>.
    /// The model binder must reject it and return <c>400 InvalidRequest</c>.
    /// </summary>
    [Fact]
    public async Task Search_UnknownSortValue_Returns400()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?sort=downloads");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        Assert.True(doc.RootElement.TryGetProperty("error", out var errorProp),
            "400 response must include an 'error' field.");
        Assert.Equal("InvalidRequest", errorProp.GetString());
    }

    // ── Default (q/page/pageSize only) behavior preserved ────────────────────

    /// <summary>
    /// A request with only <c>q</c>, <c>page</c>, and <c>pageSize</c> must behave
    /// exactly as before P5 — sorting by name ascending (Relevance default).
    /// </summary>
    [Fact]
    public async Task Search_DefaultParameters_ReturnsResultsOrderedByName()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        await SeedPackageAsync(factory, MakePackage("z-last-pkg"));
        await SeedPackageAsync(factory, MakePackage("a-first-pkg"));
        await SeedPackageAsync(factory, MakePackage("m-middle-pkg"));

        var response = await client.GetAsync("/api/v1/search");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("a-first-pkg", items[0].GetProperty("name").GetString());
        Assert.Equal("m-middle-pkg", items[1].GetProperty("name").GetString());
        Assert.Equal("z-last-pkg", items[2].GetProperty("name").GetString());
    }
}
