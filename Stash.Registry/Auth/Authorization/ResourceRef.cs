namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Discriminated union of resource kinds that the PDP can evaluate.
/// </summary>
public abstract record ResourceRef;

/// <summary>
/// A scoped package — the primary resource for package-related actions.
/// </summary>
/// <param name="Scope">The bare scope name without the leading <c>@</c>.</param>
/// <param name="LocalName">The package name within the scope (no scope prefix).</param>
/// <remarks>
/// The canonical package identifier is <c>@{Scope}/{LocalName}</c>.
/// This record is always constructed from the URL route, never from the manifest body.
/// </remarks>
public sealed record PackageResource(string Scope, string LocalName) : ResourceRef
{
    /// <summary>Returns the full scoped package name <c>@{Scope}/{LocalName}</c>.</summary>
    public string FullName => $"@{Scope}/{LocalName}";

    /// <summary>
    /// The specific version being accessed, when the route carries a <c>{version}</c> segment.
    /// <see langword="null"/> for package-level (non-version) routes.
    /// Present only for <see cref="RegistryAction.ReadPackageVersion"/> and
    /// <see cref="RegistryAction.DownloadPackageVersion"/> routes — the filter uses this to
    /// restore the pre-refactor version-scoped 404 message.
    /// </summary>
    public string? Version { get; init; }
}

/// <summary>A scope resource, used for scope-management actions.</summary>
/// <param name="Scope">The bare scope name without the leading <c>@</c>.</param>
public sealed record ScopeResource(string Scope) : ResourceRef;

/// <summary>An organisation resource, used for org-management actions.</summary>
/// <param name="OrgName">The organisation name.</param>
public sealed record OrgResource(string OrgName) : ResourceRef;

/// <summary>A token resource, used for token-management actions.</summary>
/// <param name="TokenId">The JTI of the token being acted upon.</param>
public sealed record TokenResource(string TokenId) : ResourceRef;

/// <summary>The admin resource plane — used for admin-only operations.</summary>
public sealed record AdminResource() : ResourceRef;

/// <summary>The search resource plane — used for search operations.</summary>
public sealed record SearchResource() : ResourceRef;

/// <summary>
/// A self-reference resource used by whoami and self-service token operations
/// where the resource is the authenticated principal itself.
/// </summary>
public sealed record PrincipalSelfResource() : ResourceRef;
