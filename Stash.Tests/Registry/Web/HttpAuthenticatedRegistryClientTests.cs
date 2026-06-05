using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Auth;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Unit tests for <see cref="HttpAuthenticatedRegistryClient"/>.
/// </summary>
public sealed class HttpAuthenticatedRegistryClientTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string FixtureJwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.fixture";

    private static (HttpAuthenticatedRegistryClient Client, List<HttpRequestMessage> CapturedRequests)
        CreateClientWithCapture(
            Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var captured = new List<HttpRequestMessage>();

        var messageHandler = new FakeMessageHandler(req =>
        {
            captured.Add(req);
            return handler(req);
        });

        var services = new ServiceCollection();
        services.AddHttpClient(AuthenticatedRegistryHttpClients.AuthenticatedRegistry, client =>
        {
            // Base address needed so relative request URIs are resolved correctly.
            client.BaseAddress = new Uri("https://registry.example.com");
        }).ConfigurePrimaryHttpMessageHandler(() => messageHandler);

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();

        var session = new BffSession
        {
            Username = "alice",
            PublishTokenJwt = FixtureJwt,
            PublishTokenId = "tok-abc",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        };

        var accessor = new FixtureSessionTokenAccessor(session);
        var client = new HttpAuthenticatedRegistryClient(factory, accessor);

        return (client, captured);
    }

    private static HttpResponseMessage JsonOk<T>(T value) =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static HttpResponseMessage JsonCreated<T>(T value) =>
        new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(value) };

    // ── Constructor: fail-closed backstop ─────────────────────────────────────

    [Fact]
    public void Constructor_NoActiveSession_ThrowsNoActiveSessionException()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(AuthenticatedRegistryHttpClients.AuthenticatedRegistry, client =>
        {
            client.BaseAddress = new Uri("https://registry.example.com");
        });
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var emptyAccessor = new FixtureSessionTokenAccessor(null);

        Assert.Throws<NoActiveSessionException>(() =>
            new HttpAuthenticatedRegistryClient(factory, emptyAccessor));
    }

    // ── Authorization header on every method ──────────────────────────────────

    [Fact]
    public async Task SearchOwnedAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            JsonOk(new PagedResponse<PackageSummaryResponse>
            {
                Items = [],
                TotalCount = 0,
                Page = 1,
                PageSize = 20,
                TotalPages = 0,
            }));

        await client.SearchOwnedAsync(new SearchQuery { owner = "alice" });

        Assert.Single(captured);
        var auth = captured[0].Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal(FixtureJwt, auth.Parameter);
    }

    [Fact]
    public async Task GetPackageAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            JsonOk(StubPackageDetail()));

        await client.GetPackageAsync("org", "my-lib");

        Assert.Single(captured);
        AssertBearerToken(captured[0]);
    }

    [Fact]
    public async Task WhoamiAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            JsonOk(new WhoamiResponse { Username = "alice", Role = UserRoles.User }));

        await client.WhoamiAsync();

        Assert.Single(captured);
        AssertBearerToken(captured[0]);
    }

    [Fact]
    public async Task ListTokensAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            JsonOk(new TokenListResponse { Tokens = [] }));

        await client.ListTokensAsync();

        Assert.Single(captured);
        AssertBearerToken(captured[0]);
    }

    [Fact]
    public async Task CreateTokenAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            JsonCreated(new TokenCreateResponse
            {
                Token = "new-tok",
                TokenId = "tid-001",
                Scope = TokenScopes.Publish,
                ExpiresAt = DateTime.UtcNow.AddHours(8),
            }));

        await client.CreateTokenAsync(new TokenCreateRequest
        {
            Ceiling = TokenScopes.Publish.ToWire(),
            Name = "test token",
            ExpiresIn = "8h",
        });

        Assert.Single(captured);
        AssertBearerToken(captured[0]);
    }

    [Fact]
    public async Task RevokeTokenAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.RevokeTokenAsync("tok-001");

        Assert.Single(captured);
        AssertBearerToken(captured[0]);
        Assert.Equal(HttpMethod.Delete, captured[0].Method);
    }

    [Fact]
    public async Task DeprecatePackageAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            JsonOk(new DeprecationResponse { Ok = true, Package = "org/my-lib", Deprecated = true }));

        await client.DeprecatePackageAsync("org", "my-lib",
            new DeprecatePackageRequest { Message = "Use new-lib instead." });

        Assert.Single(captured);
        AssertBearerToken(captured[0]);
    }

    [Fact]
    public async Task DeprecateVersionAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            JsonOk(new DeprecationResponse { Ok = true, Package = "org/my-lib", Version = "1.0.0", Deprecated = true }));

        await client.DeprecateVersionAsync("org", "my-lib", "1.0.0",
            new DeprecateVersionRequest { Message = "Use 2.0.0 instead." });

        Assert.Single(captured);
        AssertBearerToken(captured[0]);
    }

    [Fact]
    public async Task UndeprecatePackageAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            JsonOk(new DeprecationResponse { Ok = true, Package = "org/my-lib", Deprecated = false }));

        await client.UndeprecatePackageAsync("org", "my-lib");

        Assert.Single(captured);
        AssertBearerToken(captured[0]);
        Assert.Equal(HttpMethod.Delete, captured[0].Method);
    }

    [Fact]
    public async Task UndeprecateVersionAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            JsonOk(new DeprecationResponse { Ok = true, Package = "org/my-lib", Version = "1.0.0", Deprecated = false }));

        await client.UndeprecateVersionAsync("org", "my-lib", "1.0.0");

        Assert.Single(captured);
        AssertBearerToken(captured[0]);
        Assert.Equal(HttpMethod.Delete, captured[0].Method);
    }

    [Fact]
    public async Task SetVisibilityAsync_SetsAuthorizationHeader()
    {
        var (client, captured) = CreateClientWithCapture(_ =>
            JsonOk(new SetVisibilityResponse
            {
                Ok = true,
                Package = "org/my-lib",
                Visibility = Visibilities.Private,
            }));

        await client.SetVisibilityAsync("org", "my-lib",
            new SetVisibilityRequest { Visibility = Visibilities.Private });

        Assert.Single(captured);
        AssertBearerToken(captured[0]);
    }

    // ── DI factory resolution (fail-closed backstop) ──────────────────────────

    [Fact]
    public void DiFactory_NoSession_ThrowsNoActiveSessionException()
    {
        // Simulate what Program.cs does: the factory calls TryGetSession and throws if false.
        var services = new ServiceCollection();
        services.AddHttpClient(AuthenticatedRegistryHttpClients.AuthenticatedRegistry, client =>
        {
            client.BaseAddress = new Uri("https://registry.example.com");
        });
        services.AddSingleton<ISessionTokenAccessor>(new FixtureSessionTokenAccessor(null));
        services.AddScoped<IAuthenticatedRegistryClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var sessionTokenAccessor = sp.GetRequiredService<ISessionTokenAccessor>();
            return new HttpAuthenticatedRegistryClient(httpClientFactory, sessionTokenAccessor);
        });

        var sp = services.BuildServiceProvider();

        // Creating a scope simulates per-request resolution.
        using var scope = sp.CreateScope();
        Assert.Throws<NoActiveSessionException>(() =>
            scope.ServiceProvider.GetRequiredService<IAuthenticatedRegistryClient>());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AssertBearerToken(HttpRequestMessage request)
    {
        var auth = request.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal(FixtureJwt, auth.Parameter);
    }

    private static PackageDetailResponse StubPackageDetail() => new()
    {
        Name = "org/my-lib",
        Keywords = [],
        Versions = new Dictionary<string, VersionDetailResponse>(),
        CreatedAt = "2026-01-01T00:00:00Z",
        UpdatedAt = "2026-06-04T00:00:00Z",
    };

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class FakeMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public FakeMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    /// <summary>
    /// A minimal <see cref="ISessionTokenAccessor"/> that returns a fixed session (or null).
    /// </summary>
    private sealed class FixtureSessionTokenAccessor : ISessionTokenAccessor
    {
        private readonly BffSession? _session;
        public FixtureSessionTokenAccessor(BffSession? session) => _session = session;

        public bool TryGetSession(out BffSession? session)
        {
            session = _session;
            return session is not null;
        }

        public Task<string> GetPublishTokenAsync(CancellationToken cancellationToken = default)
        {
            if (_session is null) throw new NoActiveSessionException();
            return Task.FromResult(_session.PublishTokenJwt);
        }
    }
}
