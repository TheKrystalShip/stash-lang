using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Services;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Named HTTP client used by <see cref="HttpAuthenticatedRegistryClient"/>.
/// Listed on the chokepoint allowlist in <c>AuthClientChokepointMetaTests</c>.
/// </summary>
public static class AuthenticatedRegistryHttpClients
{
    /// <summary>
    /// Named client used by <see cref="HttpAuthenticatedRegistryClient"/>.
    /// The base address comes from <c>RegistryClientConfig.BaseUrl</c>.
    /// Every call through this client sets <c>Authorization: Bearer &lt;publishJwt&gt;</c>
    /// per-request — the chokepoint is enforced inside each method body.
    /// </summary>
    public const string AuthenticatedRegistry = "AuthenticatedRegistry";
}

/// <summary>
/// <see cref="IAuthenticatedRegistryClient"/> implementation backed by
/// <see cref="IHttpClientFactory"/> and <see cref="ISessionTokenAccessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>C1 token-threading chokepoint.</b> Every method creates an <see cref="HttpRequestMessage"/>,
/// attaches <c>Authorization: Bearer &lt;publishJwt&gt;</c>, and sends via
/// <c>SendAsync</c>. There is no code path that sends a request without the header.
/// </para>
/// <para>
/// The constructor calls <see cref="ISessionTokenAccessor.TryGetSession"/> and throws
/// <see cref="NoActiveSessionException"/> if no session is in scope — the DI factory's
/// fail-closed backstop. This throw should never fire on a normal anonymous request because
/// the <c>[Authorize]</c> page convention 302s before the page model is constructed.
/// </para>
/// </remarks>
public sealed class HttpAuthenticatedRegistryClient : IAuthenticatedRegistryClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _publishTokenJwt;

    public HttpAuthenticatedRegistryClient(
        IHttpClientFactory httpClientFactory,
        ISessionTokenAccessor sessionTokenAccessor)
    {
        _httpClientFactory = httpClientFactory;

        // Fail-closed backstop: throw if no session is in scope.
        // This is the DI chokepoint — normal anonymous paths never reach here because
        // MaintainerAreaConventions applies [Authorize] which 302s before page-model construction.
        if (!sessionTokenAccessor.TryGetSession(out var session) || session is null)
            throw new NoActiveSessionException();

        _publishTokenJwt = session.PublishTokenJwt;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a fresh <see cref="HttpClient"/> from the named factory.</summary>
    private HttpClient CreateClient() =>
        _httpClientFactory.CreateClient(AuthenticatedRegistryHttpClients.AuthenticatedRegistry);

    /// <summary>Escapes a single URL path segment.</summary>
    private static string Seg(string segment) => Uri.EscapeDataString(segment);

    /// <summary>
    /// Creates an <see cref="HttpRequestMessage"/> with the Authorization header set.
    /// This is the single place where the bearer token is attached — every method uses this helper.
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        // C1 chokepoint: Authorization header is always present — no opt-out.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _publishTokenJwt);
        return request;
    }

    /// <summary>
    /// Sends a GET request and deserializes the response body.
    /// Returns <see langword="null"/> for 404; throws <see cref="RegistryClientException"/> for other errors.
    /// </summary>
    private async Task<T?> GetNullableAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        var client = CreateClient();
        using var request = CreateRequest(HttpMethod.Get, url);
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a GET request and deserializes a required response body.
    /// Throws <see cref="RegistryClientException"/> on any non-success response.
    /// </summary>
    private async Task<T> GetRequiredAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        var client = CreateClient();
        using var request = CreateRequest(HttpMethod.Get, url);
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken).ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException($"Registry returned an empty body for {url}.");
    }

    /// <summary>
    /// Sends a POST with JSON body and deserializes a required response body.
    /// </summary>
    private async Task<T> PostJsonAsync<TBody, T>(string url, TBody body, CancellationToken cancellationToken)
        where T : class
    {
        var client = CreateClient();
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken).ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException($"Registry returned an empty body for POST {url}.");
    }

    /// <summary>
    /// Sends a PATCH with JSON body and deserializes a required response body.
    /// </summary>
    private async Task<T> PatchJsonAsync<TBody, T>(string url, TBody body, CancellationToken cancellationToken)
        where T : class
    {
        var client = CreateClient();
        using var request = CreateRequest(HttpMethod.Patch, url);
        request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken).ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException($"Registry returned an empty body for PATCH {url}.");
    }

    /// <summary>
    /// Sends a DELETE request. Throws <see cref="RegistryClientException"/> on non-success.
    /// </summary>
    private async Task DeleteAsync(string url, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        using var request = CreateRequest(HttpMethod.Delete, url);
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ThrowRegistryExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? errorCode = null;
        string? errorMessage = null;

        try
        {
            var body = await response.Content
                .ReadFromJsonAsync<ErrorResponse>(cancellationToken)
                .ConfigureAwait(false);
            errorCode = body?.Error;
            errorMessage = body?.Message;
        }
        catch
        {
            // Body was not a valid ErrorResponse JSON; surface the status code alone.
        }

        throw new RegistryClientException(response.StatusCode, errorCode, errorMessage);
    }

    // ── IAuthenticatedRegistryClient implementation ───────────────────────────

    /// <inheritdoc/>
    public async Task<PagedResponse<PackageSummaryResponse>> SearchOwnedAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var parameters = new System.Collections.Generic.Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(query.q)) parameters["q"] = query.q;
        if (!string.IsNullOrEmpty(query.keyword)) parameters["keyword"] = query.keyword;
        if (!string.IsNullOrEmpty(query.license)) parameters["license"] = query.license;
        if (query.deprecated.HasValue) parameters["deprecated"] = query.deprecated.Value ? "true" : "false";
        if (!string.IsNullOrEmpty(query.owner)) parameters["owner"] = query.owner;
        if (query.sort != PackageSortOrder.Relevance) parameters["sort"] = query.sort.ToString();
        if (query.page != 1) parameters["page"] = query.page.ToString();
        if (query.pageSize != 20) parameters["pageSize"] = query.pageSize.ToString();

        var url = QueryHelpers.AddQueryString("/api/v1/search", parameters);

        var client = CreateClient();
        using var request = CreateRequest(HttpMethod.Get, url);
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken).ConfigureAwait(false);

        var result = await response.Content
            .ReadFromJsonAsync<PagedResponse<PackageSummaryResponse>>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Registry returned an empty body for search.");
    }

    /// <inheritdoc/>
    public Task<PackageDetailResponse?> GetPackageAsync(
        string scope,
        string name,
        CancellationToken cancellationToken = default)
        => GetNullableAsync<PackageDetailResponse>(
            $"/api/v1/packages/{Seg(scope)}/{Seg(name)}",
            cancellationToken);

    /// <inheritdoc/>
    public Task<WhoamiResponse> WhoamiAsync(CancellationToken cancellationToken = default)
        => GetRequiredAsync<WhoamiResponse>("/api/v1/auth/whoami", cancellationToken);

    /// <inheritdoc/>
    public Task<TokenListResponse> ListTokensAsync(CancellationToken cancellationToken = default)
        => GetRequiredAsync<TokenListResponse>("/api/v1/auth/tokens", cancellationToken);

    /// <inheritdoc/>
    public Task<TokenCreateResponse> CreateTokenAsync(
        TokenCreateRequest request,
        CancellationToken cancellationToken = default)
        => PostJsonAsync<TokenCreateRequest, TokenCreateResponse>(
            "/api/v1/auth/tokens",
            request,
            cancellationToken);

    /// <inheritdoc/>
    public Task RevokeTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        => DeleteAsync(
            $"/api/v1/auth/tokens/{Seg(tokenId)}",
            cancellationToken);

    /// <inheritdoc/>
    public Task<DeprecationResponse> DeprecatePackageAsync(
        string scope,
        string name,
        DeprecatePackageRequest request,
        CancellationToken cancellationToken = default)
        => PatchJsonAsync<DeprecatePackageRequest, DeprecationResponse>(
            $"/api/v1/packages/{Seg(scope)}/{Seg(name)}/deprecate",
            request,
            cancellationToken);

    /// <inheritdoc/>
    public async Task<DeprecationResponse> UndeprecatePackageAsync(
        string scope,
        string name,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var request = CreateRequest(HttpMethod.Delete, $"/api/v1/packages/{Seg(scope)}/{Seg(name)}/deprecate");
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken).ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<DeprecationResponse>(cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Registry returned an empty body for undeprecate package.");
    }

    /// <inheritdoc/>
    public Task<DeprecationResponse> DeprecateVersionAsync(
        string scope,
        string name,
        string version,
        DeprecateVersionRequest request,
        CancellationToken cancellationToken = default)
        => PatchJsonAsync<DeprecateVersionRequest, DeprecationResponse>(
            $"/api/v1/packages/{Seg(scope)}/{Seg(name)}/{Seg(version)}/deprecate",
            request,
            cancellationToken);

    /// <inheritdoc/>
    public async Task<DeprecationResponse> UndeprecateVersionAsync(
        string scope,
        string name,
        string version,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var request = CreateRequest(HttpMethod.Delete, $"/api/v1/packages/{Seg(scope)}/{Seg(name)}/{Seg(version)}/deprecate");
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken).ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<DeprecationResponse>(cancellationToken).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("Registry returned an empty body for undeprecate version.");
    }

    /// <inheritdoc/>
    public Task<SetVisibilityResponse> SetVisibilityAsync(
        string scope,
        string name,
        SetVisibilityRequest request,
        CancellationToken cancellationToken = default)
        => PatchJsonAsync<SetVisibilityRequest, SetVisibilityResponse>(
            $"/api/v1/packages/{Seg(scope)}/{Seg(name)}/visibility",
            request,
            cancellationToken);
}
