using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Configuration;

namespace Stash.Registry.Web.Services;

/// <summary>
/// <see cref="IRegistryClient"/> implementation backed by <see cref="IHttpClientFactory"/>.
/// Base address comes from <see cref="RegistryClientConfig.BaseUrl"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>HTTP 404 → returns <see langword="null"/> (for nullable-return methods).</item>
///   <item>Any other non-success status → throws <see cref="RegistryClientException"/>.</item>
///   <item>No <c>Authorization</c> header is sent — anonymous browse only.</item>
/// </list>
/// </remarks>
public sealed class HttpRegistryClient : IRegistryClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>The named HTTP client used by this service.</summary>
    public const string HttpClientName = "RegistryClient";

    public HttpRegistryClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

    /// <summary>
    /// Escapes a single path segment for use in a registry URL.
    /// Applies <see cref="Uri.EscapeDataString"/> so characters like <c>/</c> and <c>%</c>
    /// in scope or name values do not corrupt the path.
    /// </summary>
    private static string Seg(string segment) => Uri.EscapeDataString(segment);

    /// <summary>
    /// Sends a GET request and returns the deserialized response body.
    /// Returns <see langword="null"/> when the registry returns 404.
    /// Throws <see cref="RegistryClientException"/> for all other non-success status codes.
    /// </summary>
    private async Task<T?> GetNullableAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        var client = CreateClient();
        var response = await client.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    /// <summary>
    /// Sends a GET request and returns the deserialized response body.
    /// Throws <see cref="RegistryClientException"/> for all non-success status codes
    /// (including 404, which is unexpected for endpoints like <c>.well-known/registry</c>).
    /// </summary>
    private async Task<T> GetRequiredAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        var client = CreateClient();
        var response = await client.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        return result ?? throw new InvalidOperationException($"Registry returned an empty body for {url}.");
    }

    private static async Task ThrowRegistryExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? errorCode = null;
        string? errorMessage = null;

        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(ct);
            errorCode = body?.Error;
            errorMessage = body?.Message;
        }
        catch
        {
            // Body was not a valid ErrorResponse JSON; surface the status code alone.
        }

        throw new RegistryClientException(response.StatusCode, errorCode, errorMessage);
    }

    // ── IRegistryClient implementation ───────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PagedResponse<PackageSummaryResponse>> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>();
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
        var response = await client.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
            await ThrowRegistryExceptionAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<PagedResponse<PackageSummaryResponse>>(cancellationToken);
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
    public Task<PagedResponse<VersionDetailResponse>?> GetVersionsAsync(
        string scope,
        string name,
        VersionsQuery query,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>();
        if (query.page != 1) parameters["page"] = query.page.ToString();
        if (query.pageSize != 20) parameters["pageSize"] = query.pageSize.ToString();

        var url = QueryHelpers.AddQueryString(
            $"/api/v1/packages/{Seg(scope)}/{Seg(name)}/versions",
            parameters);

        return GetNullableAsync<PagedResponse<VersionDetailResponse>>(url, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ReadmeResponse?> GetReadmeAsync(
        string scope,
        string name,
        CancellationToken cancellationToken = default)
        => GetNullableAsync<ReadmeResponse>(
            $"/api/v1/packages/{Seg(scope)}/{Seg(name)}/readme",
            cancellationToken);

    /// <inheritdoc/>
    public Task<VersionDetailResponse?> GetVersionAsync(
        string scope,
        string name,
        string version,
        CancellationToken cancellationToken = default)
        => GetNullableAsync<VersionDetailResponse>(
            $"/api/v1/packages/{Seg(scope)}/{Seg(name)}/{Seg(version)}",
            cancellationToken);

    /// <inheritdoc/>
    public Task<DiscoveryResponse> GetDiscoveryAsync(CancellationToken cancellationToken = default)
        => GetRequiredAsync<DiscoveryResponse>("/api/v1/.well-known/registry", cancellationToken);
}
