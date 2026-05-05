using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stash.Cli.PackageManager;
using Xunit;

namespace Stash.Tests.Cli;

[Collection("CliTests")]
public class RegistryClientAuthTests
{
    // ---------------------------------------------------------------------------
    // Test helpers
    // ---------------------------------------------------------------------------

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public FakeHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("connection refused");
        }
    }

    /// <summary>
    /// Builds a RegistryClient whose login response contains ONLY 'accessToken' (no 'token' alias).
    /// </summary>
    private static RegistryClient BuildLoginClient(string loginResponseJson)
    {
        var http = new HttpClient(new FakeHandler(HttpStatusCode.OK, loginResponseJson));
        return new RegistryClient("https://registry.example.com", http);
    }

    /// <summary>
    /// Builds a RegistryClient pre-loaded with a token that expires within 3 minutes,
    /// so EnsureTokenFresh triggers a refresh attempt on the next call.
    /// </summary>
    private static RegistryClient BuildNearExpiryClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        return new RegistryClient(
            "https://registry.example.com",
            http,
            token: "access-token",
            refreshToken: "refresh-token",
            tokenExpiresAt: DateTime.UtcNow.AddMinutes(3),   // < 5-minute threshold
            machineId: "machine-123");
    }

    // ---------------------------------------------------------------------------
    // A5 — CLI reads accessToken exclusively
    // ---------------------------------------------------------------------------

    [Fact]
    public void Login_ReadsAccessToken_NotToken()
    {
        // Server returns ONLY accessToken (no legacy 'token' alias).
        const string loginResponse = """{"accessToken":"jwt.from.server","expiresAt":"2030-01-01T00:00:00Z"}""";
        var client = BuildLoginClient(loginResponse);

        var result = client.Login("alice", "password");

        Assert.NotNull(result);
        Assert.Equal("jwt.from.server", result!.Token);
    }

    // ---------------------------------------------------------------------------
    // A6 — EnsureTokenFresh surfaces failures via stderr
    // ---------------------------------------------------------------------------

    [Fact]
    public void EnsureTokenFresh_RefreshTokenExpired_PrintsWarning_AndContinues()
    {
        // Refresh endpoint returns 401 → refresh token expired.
        // Whoami then returns 401 as well (caller gets null).
        var responses = new SequencedHandler(
            new FakeHandler(HttpStatusCode.Unauthorized, """{"error":"refresh expired"}"""),   // POST /auth/tokens/refresh
            new FakeHandler(HttpStatusCode.Unauthorized, "{}")                                  // GET /auth/whoami
        );
        var client = BuildNearExpiryClient(responses);

        var originalError = Console.Error;
        var sw = new StringWriter();
        try
        {
            Console.SetError(sw);
            _ = client.Whoami();   // triggers EnsureTokenFresh then the actual call
        }
        finally
        {
            Console.SetError(originalError);
        }

        string stderr = sw.ToString();
        Assert.Contains("warning:", stderr);
        Assert.Contains("refresh token expired", stderr);
        Assert.Contains("stash pkg login", stderr);
    }

    [Fact]
    public void EnsureTokenFresh_TransientError_PrintsWarning_AndContinues()
    {
        // Refresh endpoint returns 503.
        var responses = new SequencedHandler(
            new FakeHandler(HttpStatusCode.ServiceUnavailable, """{"error":"unavailable"}"""),  // POST /auth/tokens/refresh
            new FakeHandler(HttpStatusCode.OK, """{"username":"alice"}""")                      // GET /auth/whoami
        );
        var client = BuildNearExpiryClient(responses);

        var originalError = Console.Error;
        var sw = new StringWriter();
        try
        {
            Console.SetError(sw);
            _ = client.Whoami();
        }
        finally
        {
            Console.SetError(originalError);
        }

        string stderr = sw.ToString();
        Assert.Contains("warning:", stderr);
        Assert.Contains("503", stderr);
        Assert.Contains("Continuing with existing token", stderr);
    }

    [Fact]
    public void EnsureTokenFresh_NetworkFailure_PrintsWarning_AndContinues()
    {
        // Refresh throws HttpRequestException; whoami succeeds with the stale token.
        var responses = new SequencedHandler(
            new ThrowingHandler(),                                                    // POST /auth/tokens/refresh
            new FakeHandler(HttpStatusCode.OK, """{"username":"alice"}""")           // GET /auth/whoami
        );
        var client = BuildNearExpiryClient(responses);

        var originalError = Console.Error;
        var sw = new StringWriter();
        string? username;
        try
        {
            Console.SetError(sw);
            username = client.Whoami();
        }
        finally
        {
            Console.SetError(originalError);
        }

        string stderr = sw.ToString();
        Assert.Contains("warning:", stderr);
        Assert.Contains("registry unreachable", stderr);
        Assert.Contains("Continuing with existing token", stderr);

        // Caller continues — Whoami succeeds with the stale token.
        Assert.Equal("alice", username);
    }

    // ---------------------------------------------------------------------------
    // Helper: dispatches successive requests to successive handlers
    // ---------------------------------------------------------------------------

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly HttpMessageHandler[] _handlers;
        private int _index;

        public SequencedHandler(params HttpMessageHandler[] handlers)
        {
            _handlers = handlers;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int idx = _index++;
            if (idx >= _handlers.Length)
                throw new InvalidOperationException($"SequencedHandler: unexpected request #{idx + 1}");

            // Invoke the inner handler via reflection or by calling the protected method.
            return await InvokeAsync(_handlers[idx], request, cancellationToken);
        }

        private static Task<HttpResponseMessage> InvokeAsync(HttpMessageHandler handler, HttpRequestMessage request, CancellationToken ct)
        {
            // Use the internal SendAsync overload via the public HttpMessageInvoker wrapper.
            var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
            return invoker.SendAsync(request, ct);
        }
    }
}
