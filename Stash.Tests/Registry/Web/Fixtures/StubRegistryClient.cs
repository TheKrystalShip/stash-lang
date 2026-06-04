using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Services;

namespace Stash.Tests.Registry.Web.Fixtures;

/// <summary>
/// A fully-controlled <see cref="IRegistryClient"/> stub for integration tests.
/// Configure which responses each method returns before running the test.
/// </summary>
public sealed class StubRegistryClient : IRegistryClient
{
    // ── Search ────────────────────────────────────────────────────────────────

    /// <summary>
    /// When set, <see cref="SearchAsync"/> throws this exception instead of returning a result.
    /// </summary>
    public Exception? SearchException { get; set; }

    /// <summary>
    /// The response returned by <see cref="SearchAsync"/> when <see cref="SearchException"/> is null.
    /// Defaults to an empty paged response.
    /// </summary>
    public PagedResponse<PackageSummaryResponse> SearchResult { get; set; } =
        EmptySearchResult();

    // ── GetPackage ────────────────────────────────────────────────────────────

    /// <summary>
    /// When set, <see cref="GetPackageAsync"/> throws this exception.
    /// </summary>
    public Exception? GetPackageException { get; set; }

    /// <summary>
    /// The response returned by <see cref="GetPackageAsync"/>. <c>null</c> simulates a 404.
    /// </summary>
    public PackageDetailResponse? GetPackageResult { get; set; }

    // ── GetVersions ───────────────────────────────────────────────────────────

    /// <summary>
    /// When set, <see cref="GetVersionsAsync"/> throws this exception.
    /// </summary>
    public Exception? GetVersionsException { get; set; }

    /// <summary>
    /// The response returned by <see cref="GetVersionsAsync"/>. <c>null</c> simulates a 404.
    /// </summary>
    public PagedResponse<VersionDetailResponse>? GetVersionsResult { get; set; }

    // ── GetReadme ─────────────────────────────────────────────────────────────

    /// <summary>
    /// When set, <see cref="GetReadmeAsync"/> throws this exception.
    /// </summary>
    public Exception? GetReadmeException { get; set; }

    /// <summary>
    /// The response returned by <see cref="GetReadmeAsync"/>. <c>null</c> simulates a 404.
    /// </summary>
    public ReadmeResponse? GetReadmeResult { get; set; }

    // ── GetVersion ────────────────────────────────────────────────────────────

    /// <summary>
    /// When set, <see cref="GetVersionAsync"/> throws this exception.
    /// </summary>
    public Exception? GetVersionException { get; set; }

    /// <summary>
    /// The response returned by <see cref="GetVersionAsync"/>. <c>null</c> simulates a 404.
    /// </summary>
    public VersionDetailResponse? GetVersionResult { get; set; }

    // ── GetDiscovery ──────────────────────────────────────────────────────────

    /// <summary>
    /// When set, <see cref="GetDiscoveryAsync"/> throws this exception.
    /// </summary>
    public Exception? GetDiscoveryException { get; set; }

    /// <summary>
    /// The response returned by <see cref="GetDiscoveryAsync"/>. Defaults to a minimal discovery response.
    /// </summary>
    public DiscoveryResponse GetDiscoveryResult { get; set; } = DefaultDiscoveryResponse();

    // ── IRegistryClient implementation ────────────────────────────────────────

    public Task<PagedResponse<PackageSummaryResponse>> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (SearchException is not null)
            throw SearchException;

        return Task.FromResult(SearchResult);
    }

    public Task<PackageDetailResponse?> GetPackageAsync(
        string scope,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (GetPackageException is not null)
            throw GetPackageException;

        return Task.FromResult(GetPackageResult);
    }

    public Task<PagedResponse<VersionDetailResponse>?> GetVersionsAsync(
        string scope,
        string name,
        VersionsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (GetVersionsException is not null)
            throw GetVersionsException;

        return Task.FromResult(GetVersionsResult);
    }

    public Task<ReadmeResponse?> GetReadmeAsync(
        string scope,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (GetReadmeException is not null)
            throw GetReadmeException;

        return Task.FromResult(GetReadmeResult);
    }

    public Task<VersionDetailResponse?> GetVersionAsync(
        string scope,
        string name,
        string version,
        CancellationToken cancellationToken = default)
    {
        if (GetVersionException is not null)
            throw GetVersionException;

        return Task.FromResult(GetVersionResult);
    }

    public Task<DiscoveryResponse> GetDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        if (GetDiscoveryException is not null)
            throw GetDiscoveryException;

        return Task.FromResult(GetDiscoveryResult);
    }

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>Creates a <see cref="StubRegistryClient"/> pre-configured to simulate a 5xx error on search.</summary>
    public static StubRegistryClient WithSearchServerError()
    {
        return new StubRegistryClient
        {
            SearchException = new RegistryClientException(System.Net.HttpStatusCode.ServiceUnavailable),
        };
    }

    /// <summary>Creates a <see cref="StubRegistryClient"/> returning a list of sample packages on search.</summary>
    public static StubRegistryClient WithPackages(IEnumerable<PackageSummaryResponse> packages)
    {
        var items = new List<PackageSummaryResponse>(packages);
        return new StubRegistryClient
        {
            SearchResult = new PagedResponse<PackageSummaryResponse>
            {
                Items = items,
                TotalCount = items.Count,
                Page = 1,
                PageSize = 20,
                TotalPages = 1,
            },
        };
    }

    private static PagedResponse<PackageSummaryResponse> EmptySearchResult() =>
        new()
        {
            Items = new List<PackageSummaryResponse>(),
            TotalCount = 0,
            Page = 1,
            PageSize = 20,
            TotalPages = 0,
        };

    private static DiscoveryResponse DefaultDiscoveryResponse() =>
        new()
        {
            Name = "Stash Registry (Test Stub)",
            ApiVersion = "v1",
            BasePath = "/api/v1",
            Limits = new DiscoveryLimits { MaxPackageSize = 52_428_800, MaxPageSize = 100 },
            Links = new DiscoveryLinks
            {
                Search = "/api/v1/search",
                Packages = "/api/v1/packages",
                OpenApi = "/openapi/v1.json",
                WellKnown = "/api/v1/.well-known/registry",
            },
            Features = new DiscoveryFeatures(),
        };

    /// <summary>Creates a sample <see cref="PackageSummaryResponse"/> for tests.</summary>
    public static PackageSummaryResponse SamplePackage(
        string name = "org/sample-pkg",
        string? description = "A sample package",
        string? latest = "1.0.0",
        string? license = "MIT",
        int ownerCount = 1,
        bool deprecated = false) =>
        new()
        {
            Name = name,
            Description = description,
            Latest = latest,
            Keywords = new List<string>(),
            UpdatedAt = "2026-06-04T12:00:00Z",
            Deprecated = deprecated,
            License = license,
            OwnerCount = ownerCount,
        };
}
