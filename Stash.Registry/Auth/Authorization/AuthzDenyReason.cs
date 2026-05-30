namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Typed reason for an authorization denial from the registry Policy Decision Point (PDP).
/// </summary>
/// <remarks>
/// The fine-grained token-capability deny reason from the prior revision is deliberately absent;
/// it would ship with the deferred capability feature (see backlog:
/// <c>.kanban/0-backlog/registry/Fine-grained token capabilities (deferred from authz-pipeline).md</c>).
/// </remarks>
public enum AuthzDenyReason
{
    /// <summary>No denial — used when <see cref="AuthzDecision.Allowed"/> is <c>true</c>.</summary>
    None,

    /// <summary>The request carries no valid principal (anonymous caller on an action that requires auth).</summary>
    NotAuthenticated,

    /// <summary>The token's coarse ceiling is insufficient for the requested action.</summary>
    TokenScopeInsufficient,

    /// <summary>The token's <c>exp</c> claim is in the past.</summary>
    TokenExpired,

    /// <summary>The token's JTI has been revoked.</summary>
    TokenRevoked,

    /// <summary>The target scope is not owned by the caller.</summary>
    ScopeNotOwned,

    /// <summary>The target scope is a reserved system scope (<c>stash</c>, <c>admin</c>).</summary>
    ScopeReserved,

    /// <summary>The caller does not hold the minimum required package role.</summary>
    PackageRoleInsufficient,

    /// <summary>The requested package does not exist.</summary>
    PackageNotFound,

    /// <summary>The action requires org membership that the caller does not have.</summary>
    OrgMembershipRequired,

    /// <summary>
    /// The package is private or internal and the caller does not have visibility rights.
    /// Controllers MUST translate this to <c>404 Not Found</c> to avoid leaking package existence.
    /// </summary>
    VisibilityHidden,

    /// <summary>A deploy-time policy (e.g. <c>ScopeOwnershipPolicy</c>) denied the request.</summary>
    PolicyDenied
}
