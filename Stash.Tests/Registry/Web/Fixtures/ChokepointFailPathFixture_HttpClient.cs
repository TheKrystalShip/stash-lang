namespace Stash.Tests.Registry.Web.Fixtures;

/// <summary>
/// Synthetic fixture used by <see cref="AuthClientChokepointMetaTests"/> self-tests.
/// Provides known-bad and known-good C# snippet strings that exercise the
/// <c>HttpClient</c> chokepoint scanner, proving it has teeth and does not pass vacuously.
/// </summary>
/// <remarks>
/// These are in-memory snippet strings — NOT actual C# files added to the Web project tree —
/// fed directly to the scan helper. No real rogue <c>HttpClient</c> call site is introduced
/// by this fixture.
/// </remarks>
internal static class ChokepointFailPathFixture_HttpClient
{
    // ── Known-bad snippet: rogue SendAsync call outside an allowed type ────────

    /// <summary>
    /// A C# snippet containing a rogue <c>SendAsync</c> call site OUTSIDE the allowed types.
    /// The chokepoint scanner MUST flag this as a violation.
    /// The receiver is deliberately named <c>http</c> (not <c>client</c>) to prove the scan
    /// discriminates by enclosing type, not receiver variable name.
    /// </summary>
    public const string RogueSendAsyncSnippet = """
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;

        // RogueService is NOT on the allowed list.
        public sealed class RogueService
        {
            private readonly IHttpClientFactory _factory;
            public RogueService(IHttpClientFactory factory) => _factory = factory;

            public async Task DoSomethingAsync(CancellationToken ct)
            {
                // Receiver named "http" (not "client") — proves the scan does NOT use the
                // receiver name as a discriminator; it uses the enclosing type name.
                var http = _factory.CreateClient("SomeClient");
                var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/search");
                var response = await http.SendAsync(request, ct);
            }
        }
        """;

    // ── Known-bad snippet: rogue extension-method calls outside an allowed type ─

    /// <summary>
    /// A C# snippet containing rogue <c>PostAsJsonAsync</c> and <c>GetFromJsonAsync</c>
    /// calls OUTSIDE the allowed types. The chokepoint scanner MUST flag both as violations.
    /// This proves the scan covers the <c>System.Net.Http.Json</c> extension-method surface,
    /// not just the six methods defined directly on <c>HttpClient</c>.
    /// </summary>
    public const string RogueExtensionMethodSnippet = """
        using System.Net.Http;
        using System.Net.Http.Json;
        using System.Threading;
        using System.Threading.Tasks;

        // RoguePageModel is NOT on the allowed list.
        public sealed class RoguePageModel
        {
            private readonly IHttpClientFactory _factory;
            public RoguePageModel(IHttpClientFactory factory) => _factory = factory;

            public async Task DoPostAsync(CancellationToken ct)
            {
                var client = _factory.CreateClient("Rogue");
                // PostAsJsonAsync must be flagged as a violation.
                var response = await client.PostAsJsonAsync("/api/v1/packages", new { Name = "test" }, ct);
            }

            public async Task DoGetAsync(CancellationToken ct)
            {
                var client = _factory.CreateClient("Rogue");
                // GetFromJsonAsync must be flagged as a violation.
                var result = await client.GetFromJsonAsync<object>("/api/v1/packages", ct);
            }
        }
        """;

    // ── Known-good snippet: SendAsync inside an allowed type ──────────────────

    /// <summary>
    /// A C# snippet whose <c>SendAsync</c> call is inside <c>HttpAuthenticatedRegistryClient</c>
    /// — the canonical always-threaded type. The scanner must NOT flag this as a violation.
    /// </summary>
    public const string AllowedSendAsyncSnippet = """
        using System.Net.Http;
        using System.Threading;
        using System.Threading.Tasks;

        // HttpAuthenticatedRegistryClient IS on the allowed list.
        public sealed class HttpAuthenticatedRegistryClient
        {
            private readonly IHttpClientFactory _factory;
            public HttpAuthenticatedRegistryClient(IHttpClientFactory factory) => _factory = factory;

            public async Task SearchAsync(CancellationToken ct)
            {
                var client = _factory.CreateClient("AuthenticatedRegistry");
                var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/search");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "token");
                // This SendAsync is inside the allowed type — no violation.
                var response = await client.SendAsync(request, ct);
            }
        }
        """;
}
