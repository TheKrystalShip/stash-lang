using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Services;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Unit tests for <see cref="HttpRegistryClient"/> against a stubbed <see cref="HttpMessageHandler"/>.
/// </summary>
/// <remarks>
/// Each test uses a hand-crafted <see cref="StubMessageHandler"/> that captures the outgoing request
/// and returns a pre-configured response — no network, no live registry.
/// </remarks>
public sealed class HttpRegistryClientTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A minimal <see cref="HttpMessageHandler"/> stub: captures the last request sent through it
    /// and always returns the pre-set response.
    /// </summary>
    private sealed class StubMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        /// <summary>The last request sent through this handler (set on first call).</summary>
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }

    /// <summary>
    /// A minimal <see cref="IHttpClientFactory"/> that always returns the same <see cref="HttpClient"/>.
    /// </summary>
    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FakeHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    /// <summary>
    /// Creates a fully-wired <see cref="HttpRegistryClient"/> backed by the given stub handler.
    /// The client has a base address so relative URLs resolve correctly.
    /// </summary>
    private static (HttpRegistryClient client, StubMessageHandler stub) BuildClient(
        HttpResponseMessage response)
    {
        var stub = new StubMessageHandler(response);
        var httpClient = new HttpClient(stub)
        {
            BaseAddress = new Uri("http://localhost:5290")
        };
        var factory = new FakeHttpClientFactory(httpClient);
        return (new HttpRegistryClient(factory), stub);
    }

    private static HttpResponseMessage JsonResponse<T>(T body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(body);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage ErrorResponse(HttpStatusCode status, string errorCode, string? message = null)
    {
        var body = new { error = errorCode, message };
        var json = JsonSerializer.Serialize(body);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    // ── GetPackageAsync — 200 ────────────────────────────────────────────────

    [Fact]
    public async Task GetPackageAsync_200_ReturnsDeserializedResponse()
    {
        var dto = new PackageDetailResponse
        {
            Name = "my-org/my-lib",
            Description = "A test package",
            Keywords = new List<string> { "test" },
            Versions = new Dictionary<string, VersionDetailResponse>(),
            CreatedAt = "2026-01-01T00:00:00Z",
            UpdatedAt = "2026-06-01T00:00:00Z",
        };

        var (client, stub) = BuildClient(JsonResponse(dto));

        var result = await client.GetPackageAsync("my-org", "my-lib");

        Assert.NotNull(result);
        Assert.Equal("my-org/my-lib", result.Name);
        Assert.Equal("A test package", result.Description);
    }

    [Fact]
    public async Task GetPackageAsync_200_RequestHasNoAuthorizationHeader()
    {
        var dto = new PackageDetailResponse
        {
            Name = "my-org/my-lib",
            Keywords = new List<string>(),
            Versions = new Dictionary<string, VersionDetailResponse>(),
            CreatedAt = "2026-01-01T00:00:00Z",
            UpdatedAt = "2026-06-01T00:00:00Z",
        };

        var (client, stub) = BuildClient(JsonResponse(dto));

        await client.GetPackageAsync("my-org", "my-lib");

        Assert.NotNull(stub.LastRequest);
        Assert.Null(stub.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task GetPackageAsync_200_RequestUrlContainsScopeAndName()
    {
        var dto = new PackageDetailResponse
        {
            Name = "my-org/my-lib",
            Keywords = new List<string>(),
            Versions = new Dictionary<string, VersionDetailResponse>(),
            CreatedAt = "2026-01-01T00:00:00Z",
            UpdatedAt = "2026-06-01T00:00:00Z",
        };

        var (client, stub) = BuildClient(JsonResponse(dto));

        await client.GetPackageAsync("my-org", "my-lib");

        Assert.NotNull(stub.LastRequest);
        var path = stub.LastRequest!.RequestUri!.PathAndQuery;
        Assert.Contains("my-org", path);
        Assert.Contains("my-lib", path);
    }

    // ── GetPackageAsync — 404 ────────────────────────────────────────────────

    [Fact]
    public async Task GetPackageAsync_404_ReturnsNull()
    {
        var (client, _) = BuildClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetPackageAsync("my-org", "nonexistent");

        Assert.Null(result);
    }

    // ── GetPackageAsync — 500 ────────────────────────────────────────────────

    [Fact]
    public async Task GetPackageAsync_500_ThrowsRegistryClientException()
    {
        var (client, _) = BuildClient(ErrorResponse(
            HttpStatusCode.InternalServerError,
            "internal_error",
            "Something went wrong"));

        var ex = await Assert.ThrowsAsync<RegistryClientException>(
            () => client.GetPackageAsync("my-org", "my-lib"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Equal("internal_error", ex.ErrorCode);
        Assert.Equal("Something went wrong", ex.ErrorMessage);
    }

    [Fact]
    public async Task GetPackageAsync_503_ThrowsRegistryClientException()
    {
        var (client, _) = BuildClient(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var ex = await Assert.ThrowsAsync<RegistryClientException>(
            () => client.GetPackageAsync("my-org", "my-lib"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    // ── SearchAsync — 200 ────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_200_ReturnsPagedResponse()
    {
        var dto = new PagedResponse<PackageSummaryResponse>
        {
            Items = new List<PackageSummaryResponse>
            {
                new PackageSummaryResponse
                {
                    Name = "my-org/my-lib",
                    Keywords = new List<string>(),
                    UpdatedAt = "2026-06-01T00:00:00Z",
                }
            },
            TotalCount = 1,
            Page = 1,
            PageSize = 20,
            TotalPages = 1,
        };

        var (client, _) = BuildClient(JsonResponse(dto));

        var result = await client.SearchAsync(new SearchQuery { q = "my-lib" });

        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("my-org/my-lib", result.Items[0].Name);
    }

    [Fact]
    public async Task SearchAsync_200_RequestHasNoAuthorizationHeader()
    {
        var dto = new PagedResponse<PackageSummaryResponse>
        {
            Items = new List<PackageSummaryResponse>(),
            TotalCount = 0,
            Page = 1,
            PageSize = 20,
            TotalPages = 0,
        };

        var (client, stub) = BuildClient(JsonResponse(dto));

        await client.SearchAsync(new SearchQuery());

        Assert.NotNull(stub.LastRequest);
        Assert.Null(stub.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task SearchAsync_500_ThrowsRegistryClientException()
    {
        var (client, _) = BuildClient(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<RegistryClientException>(
            () => client.SearchAsync(new SearchQuery()));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    // ── GetVersionsAsync — 200 / 404 ─────────────────────────────────────────

    [Fact]
    public async Task GetVersionsAsync_200_ReturnsPagedVersions()
    {
        var dto = new PagedResponse<VersionDetailResponse>
        {
            Items = new List<VersionDetailResponse>
            {
                new VersionDetailResponse
                {
                    Version = "1.0.0",
                    Dependencies = new Dictionary<string, object>(),
                    Integrity = "sha256-abc",
                    PublishedAt = "2026-01-01T00:00:00Z",
                    PublishedBy = "alice",
                }
            },
            TotalCount = 1,
            Page = 1,
            PageSize = 20,
            TotalPages = 1,
        };

        var (client, _) = BuildClient(JsonResponse(dto));

        var result = await client.GetVersionsAsync("my-org", "my-lib", new VersionsQuery());

        Assert.NotNull(result);
        Assert.Single(result!.Items);
        Assert.Equal("1.0.0", result.Items[0].Version);
    }

    [Fact]
    public async Task GetVersionsAsync_404_ReturnsNull()
    {
        var (client, _) = BuildClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetVersionsAsync("my-org", "nonexistent", new VersionsQuery());

        Assert.Null(result);
    }

    // ── GetReadmeAsync — 200 / 404 ────────────────────────────────────────────

    [Fact]
    public async Task GetReadmeAsync_200_ReturnsReadmeResponse()
    {
        var dto = new ReadmeResponse
        {
            Content = "# Hello World",
            ContentType = "text/markdown",
            ByteSize = 13,
        };

        var (client, _) = BuildClient(JsonResponse(dto));

        var result = await client.GetReadmeAsync("my-org", "my-lib");

        Assert.NotNull(result);
        Assert.Equal("# Hello World", result!.Content);
    }

    [Fact]
    public async Task GetReadmeAsync_404_ReturnsNull()
    {
        var (client, _) = BuildClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetReadmeAsync("my-org", "nonexistent");

        Assert.Null(result);
    }

    // ── GetVersionAsync — 200 / 404 / 500 ────────────────────────────────────

    [Fact]
    public async Task GetVersionAsync_200_ReturnsVersionDetail()
    {
        var dto = new VersionDetailResponse
        {
            Version = "1.2.3",
            Dependencies = new Dictionary<string, object>(),
            Integrity = "sha256-abc",
            PublishedAt = "2026-01-15T00:00:00Z",
            PublishedBy = "bob",
        };

        var (client, _) = BuildClient(JsonResponse(dto));

        var result = await client.GetVersionAsync("my-org", "my-lib", "1.2.3");

        Assert.NotNull(result);
        Assert.Equal("1.2.3", result!.Version);
        Assert.Equal("bob", result.PublishedBy);
    }

    [Fact]
    public async Task GetVersionAsync_404_ReturnsNull()
    {
        var (client, _) = BuildClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetVersionAsync("my-org", "my-lib", "9.9.9");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetVersionAsync_500_ThrowsRegistryClientException()
    {
        var (client, _) = BuildClient(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<RegistryClientException>(
            () => client.GetVersionAsync("my-org", "my-lib", "1.0.0"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    // ── GetDiscoveryAsync — 200 ───────────────────────────────────────────────

    [Fact]
    public async Task GetDiscoveryAsync_200_ReturnsDiscoveryResponse()
    {
        var dto = new DiscoveryResponse
        {
            Name = "Stash Registry",
            ApiVersion = "v1",
            BasePath = "/api/v1",
            Limits = new DiscoveryLimits { MaxPageSize = 100, MaxPackageSize = 52428800 },
            Links = new DiscoveryLinks
            {
                Search = "http://localhost:5290/api/v1/search",
                Packages = "http://localhost:5290/api/v1/packages",
                OpenApi = "http://localhost:5290/openapi/v1.json",
                WellKnown = "http://localhost:5290/api/v1/.well-known/registry",
            },
            Features = new DiscoveryFeatures
            {
                Organizations = true,
                PrivatePackages = true,
            },
        };

        var (client, stub) = BuildClient(JsonResponse(dto));

        var result = await client.GetDiscoveryAsync();

        Assert.NotNull(result);
        Assert.Equal("Stash Registry", result.Name);
        Assert.Equal("v1", result.ApiVersion);
        // No Authorization header
        Assert.NotNull(stub.LastRequest);
        Assert.Null(stub.LastRequest!.Headers.Authorization);
    }

    // ── Per-segment escaping ─────────────────────────────────────────────────

    [Theory]
    [InlineData("my-org",  "my-lib",    "/api/v1/packages/my-org/my-lib")]
    [InlineData("scope+",  "name space", "/api/v1/packages/scope%2B/name%20space")]
    [InlineData("a%b",     "c/d",        "/api/v1/packages/a%25b/c%2Fd")]
    public async Task GetPackageAsync_EscapesEachSegment(string scope, string name, string expectedPath)
    {
        var (client, stub) = BuildClient(new HttpResponseMessage(HttpStatusCode.NotFound));
        await client.GetPackageAsync(scope, name);
        Assert.Equal(expectedPath, stub.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("my-org",  "my-lib",    "/api/v1/packages/my-org/my-lib/versions")]
    [InlineData("scope+",  "name space", "/api/v1/packages/scope%2B/name%20space/versions")]
    [InlineData("a%b",     "c/d",        "/api/v1/packages/a%25b/c%2Fd/versions")]
    public async Task GetVersionsAsync_EscapesEachSegment(string scope, string name, string expectedPath)
    {
        var (client, stub) = BuildClient(new HttpResponseMessage(HttpStatusCode.NotFound));
        await client.GetVersionsAsync(scope, name, new VersionsQuery());
        Assert.Equal(expectedPath, stub.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("my-org",  "my-lib",    "/api/v1/packages/my-org/my-lib/readme")]
    [InlineData("scope+",  "name space", "/api/v1/packages/scope%2B/name%20space/readme")]
    [InlineData("a%b",     "c/d",        "/api/v1/packages/a%25b/c%2Fd/readme")]
    public async Task GetReadmeAsync_EscapesEachSegment(string scope, string name, string expectedPath)
    {
        var (client, stub) = BuildClient(new HttpResponseMessage(HttpStatusCode.NotFound));
        await client.GetReadmeAsync(scope, name);
        Assert.Equal(expectedPath, stub.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("my-org",  "my-lib",   "1.0.0",   "/api/v1/packages/my-org/my-lib/1.0.0")]
    [InlineData("scope+",  "name space", "1.0+rc1", "/api/v1/packages/scope%2B/name%20space/1.0%2Brc1")]
    [InlineData("a%b",     "c/d",       "v1%2",    "/api/v1/packages/a%25b/c%2Fd/v1%252")]
    public async Task GetVersionAsync_EscapesEachSegment(string scope, string name, string version, string expectedPath)
    {
        var (client, stub) = BuildClient(new HttpResponseMessage(HttpStatusCode.NotFound));
        await client.GetVersionAsync(scope, name, version);
        Assert.Equal(expectedPath, stub.LastRequest!.RequestUri!.AbsolutePath);
    }

    // ── RegistryClientException ───────────────────────────────────────────────

    [Fact]
    public void RegistryClientException_CarriesStatusCode_ErrorCode_ErrorMessage()
    {
        var ex = new RegistryClientException(
            HttpStatusCode.BadGateway,
            "upstream_error",
            "Registry unreachable");

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal("upstream_error", ex.ErrorCode);
        Assert.Equal("Registry unreachable", ex.ErrorMessage);
        Assert.Contains("502", ex.Message);
    }
}
