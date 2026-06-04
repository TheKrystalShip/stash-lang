using System.Net.Http.Json;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Configuration;

namespace Stash.Registry.Web.Services;

/// <summary>
/// <see cref="IRegistryClient"/> implementation backed by <see cref="IHttpClientFactory"/>.
/// Base address and timeout come from <see cref="RegistryClientConfig"/>.
/// </summary>
public sealed class HttpRegistryClient : IRegistryClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>The named HTTP client used by this service.</summary>
    public const string HttpClientName = "RegistryClient";

    public HttpRegistryClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc/>
    public async Task<DiscoveryResponse> GetDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var response = await client.GetAsync("/api/v1/.well-known/registry", cancellationToken);
        response.EnsureSuccessStatusCode();
        var discovery = await response.Content.ReadFromJsonAsync<DiscoveryResponse>(cancellationToken);
        return discovery ?? throw new InvalidOperationException("Registry returned an empty discovery response.");
    }
}
