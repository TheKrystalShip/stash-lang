using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Services;

namespace Stash.Tests.Registry.Web.Fixtures;

/// <summary>
/// A fully-controlled <see cref="IAuthenticatedRegistryClient"/> stub for integration tests.
/// Configure which responses each method returns before running the test.
/// Mirrors the shape of <see cref="StubRegistryClient"/> — same Result/Exception per-method pattern.
/// </summary>
public sealed class StubAuthenticatedRegistryClient : IAuthenticatedRegistryClient
{
    // ── SearchOwned ───────────────────────────────────────────────────────────

    public Exception? SearchOwnedException { get; set; }
    public PagedResponse<PackageSummaryResponse> SearchOwnedResult { get; set; } = EmptySearchResult();

    // ── GetPackage ────────────────────────────────────────────────────────────

    public Exception? GetPackageException { get; set; }
    public PackageDetailResponse? GetPackageResult { get; set; }

    // ── Whoami ────────────────────────────────────────────────────────────────

    public Exception? WhoamiException { get; set; }
    public WhoamiResponse WhoamiResult { get; set; } = DefaultWhoami();

    // ── ListTokens ────────────────────────────────────────────────────────────

    public Exception? ListTokensException { get; set; }
    public TokenListResponse ListTokensResult { get; set; } = new TokenListResponse();

    // ── CreateToken ───────────────────────────────────────────────────────────

    public Exception? CreateTokenException { get; set; }
    public TokenCreateResponse? CreateTokenResult { get; set; }

    // ── RevokeToken ───────────────────────────────────────────────────────────

    public Exception? RevokeTokenException { get; set; }
    public string? LastRevokedTokenId { get; set; }

    // ── DeprecatePackage ──────────────────────────────────────────────────────

    public Exception? DeprecatePackageException { get; set; }
    public DeprecationResponse? DeprecatePackageResult { get; set; }

    // ── UndeprecatePackage ────────────────────────────────────────────────────

    public Exception? UndeprecatePackageException { get; set; }
    public DeprecationResponse? UndeprecatePackageResult { get; set; }

    // ── DeprecateVersion ──────────────────────────────────────────────────────

    public Exception? DeprecateVersionException { get; set; }
    public DeprecationResponse? DeprecateVersionResult { get; set; }

    // ── UndeprecateVersion ────────────────────────────────────────────────────

    public Exception? UndeprecateVersionException { get; set; }
    public DeprecationResponse? UndeprecateVersionResult { get; set; }

    // ── SetVisibility ─────────────────────────────────────────────────────────

    public Exception? SetVisibilityException { get; set; }
    public SetVisibilityResponse? SetVisibilityResult { get; set; }

    // ── Captures (for assertion) ──────────────────────────────────────────────

    /// <summary>The last query passed to <see cref="SearchOwnedAsync"/>.</summary>
    public SearchQuery? LastSearchQuery { get; private set; }

    // ── IAuthenticatedRegistryClient implementation ───────────────────────────

    public Task<PagedResponse<PackageSummaryResponse>> SearchOwnedAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default)
    {
        LastSearchQuery = query;
        if (SearchOwnedException is not null) throw SearchOwnedException;
        return Task.FromResult(SearchOwnedResult);
    }

    public Task<PackageDetailResponse?> GetPackageAsync(
        string scope, string name,
        CancellationToken cancellationToken = default)
    {
        if (GetPackageException is not null) throw GetPackageException;
        return Task.FromResult(GetPackageResult);
    }

    public Task<WhoamiResponse> WhoamiAsync(CancellationToken cancellationToken = default)
    {
        if (WhoamiException is not null) throw WhoamiException;
        return Task.FromResult(WhoamiResult);
    }

    public Task<TokenListResponse> ListTokensAsync(CancellationToken cancellationToken = default)
    {
        if (ListTokensException is not null) throw ListTokensException;
        return Task.FromResult(ListTokensResult);
    }

    public Task<TokenCreateResponse> CreateTokenAsync(
        TokenCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (CreateTokenException is not null) throw CreateTokenException;
        if (CreateTokenResult is null)
            throw new InvalidOperationException("StubAuthenticatedRegistryClient.CreateTokenResult is not set.");
        return Task.FromResult(CreateTokenResult);
    }

    public Task RevokeTokenAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        LastRevokedTokenId = tokenId;
        if (RevokeTokenException is not null) throw RevokeTokenException;
        return Task.CompletedTask;
    }

    public Task<DeprecationResponse> DeprecatePackageAsync(
        string scope, string name,
        DeprecatePackageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (DeprecatePackageException is not null) throw DeprecatePackageException;
        if (DeprecatePackageResult is null)
            throw new InvalidOperationException("StubAuthenticatedRegistryClient.DeprecatePackageResult is not set.");
        return Task.FromResult(DeprecatePackageResult);
    }

    public Task<DeprecationResponse> UndeprecatePackageAsync(
        string scope, string name,
        CancellationToken cancellationToken = default)
    {
        if (UndeprecatePackageException is not null) throw UndeprecatePackageException;
        if (UndeprecatePackageResult is null)
            throw new InvalidOperationException("StubAuthenticatedRegistryClient.UndeprecatePackageResult is not set.");
        return Task.FromResult(UndeprecatePackageResult);
    }

    public Task<DeprecationResponse> DeprecateVersionAsync(
        string scope, string name, string version,
        DeprecateVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (DeprecateVersionException is not null) throw DeprecateVersionException;
        if (DeprecateVersionResult is null)
            throw new InvalidOperationException("StubAuthenticatedRegistryClient.DeprecateVersionResult is not set.");
        return Task.FromResult(DeprecateVersionResult);
    }

    public Task<DeprecationResponse> UndeprecateVersionAsync(
        string scope, string name, string version,
        CancellationToken cancellationToken = default)
    {
        if (UndeprecateVersionException is not null) throw UndeprecateVersionException;
        if (UndeprecateVersionResult is null)
            throw new InvalidOperationException("StubAuthenticatedRegistryClient.UndeprecateVersionResult is not set.");
        return Task.FromResult(UndeprecateVersionResult);
    }

    public Task<SetVisibilityResponse> SetVisibilityAsync(
        string scope, string name,
        SetVisibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (SetVisibilityException is not null) throw SetVisibilityException;
        if (SetVisibilityResult is null)
            throw new InvalidOperationException("StubAuthenticatedRegistryClient.SetVisibilityResult is not set.");
        return Task.FromResult(SetVisibilityResult);
    }

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>Creates a stub returning a fixed set of packages from <see cref="SearchOwnedAsync"/>.</summary>
    public static StubAuthenticatedRegistryClient WithPackages(
        IEnumerable<PackageSummaryResponse> packages)
    {
        var items = new System.Collections.Generic.List<PackageSummaryResponse>(packages);
        return new StubAuthenticatedRegistryClient
        {
            SearchOwnedResult = new PagedResponse<PackageSummaryResponse>
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
            Items = [],
            TotalCount = 0,
            Page = 1,
            PageSize = 20,
            TotalPages = 0,
        };

    private static WhoamiResponse DefaultWhoami() =>
        new() { Username = "test-user", Role = UserRoles.User };
}
