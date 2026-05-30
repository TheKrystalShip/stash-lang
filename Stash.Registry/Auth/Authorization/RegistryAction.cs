namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Closed enumeration of every action that the registry Policy Decision Point (PDP)
/// can authorise. Each controller action maps to exactly one <see cref="RegistryAction"/>.
/// </summary>
public enum RegistryAction
{
    // ── Package ───────────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/packages/{scope}/{name}</summary>
    ReadPackageMetadata,

    /// <summary>GET /api/v1/packages/{scope}/{name}/{version}</summary>
    ReadPackageVersion,

    /// <summary>GET /api/v1/packages/{scope}/{name}/{version}/download</summary>
    DownloadPackageVersion,

    /// <summary>PUT /api/v1/packages/{scope}/{name} — when the package does NOT yet exist.</summary>
    CreatePackage,

    /// <summary>PUT /api/v1/packages/{scope}/{name} — when the package DOES exist.</summary>
    PublishVersion,

    /// <summary>DELETE /api/v1/packages/{scope}/{name}/{version}</summary>
    UnpublishVersion,

    /// <summary>PATCH /api/v1/packages/{scope}/{name}/deprecate (and undeprecate)</summary>
    DeprecatePackage,

    /// <summary>PATCH /api/v1/packages/{scope}/{name}/{version}/deprecate (and undeprecate)</summary>
    DeprecateVersion,

    /// <summary>PATCH /api/v1/packages/{scope}/{name}/visibility</summary>
    ChangePackageVisibility,

    /// <summary>GET /api/v1/packages/{scope}/{name}/roles</summary>
    ListPackageRoles,

    /// <summary>PUT /api/v1/packages/{scope}/{name}/roles — owner-gated self-service assign.</summary>
    AssignPackageRole,

    /// <summary>DELETE /api/v1/packages/{scope}/{name}/roles — owner-gated self-service revoke.</summary>
    RevokePackageRole,

    /// <summary>DELETE /api/v1/packages/{scope}/{name}</summary>
    DeletePackage,

    // ── Scope ─────────────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/scopes/{scope}</summary>
    ResolveScope,

    /// <summary>POST /api/v1/scopes</summary>
    ClaimScope,

    /// <summary>POST /api/v1/scopes/{scope}/verify</summary>
    VerifyScope,

    // ── Org ───────────────────────────────────────────────────────────────────

    /// <summary>POST /api/v1/orgs</summary>
    CreateOrg,

    /// <summary>GET /api/v1/orgs/{org}</summary>
    ReadOrg,

    /// <summary>POST /api/v1/orgs/{org}/members</summary>
    AddOrgMember,

    /// <summary>DELETE /api/v1/orgs/{org}/members/{username}</summary>
    RemoveOrgMember,

    /// <summary>POST /api/v1/orgs/{org}/teams</summary>
    CreateTeam,

    /// <summary>POST /api/v1/orgs/{org}/teams/{team}/members</summary>
    AddTeamMember,

    // ── Auth / Tokens ─────────────────────────────────────────────────────────

    /// <summary>POST /api/v1/auth/login</summary>
    Login,

    /// <summary>POST /api/v1/auth/register</summary>
    Register,

    /// <summary>GET /api/v1/auth/whoami</summary>
    Whoami,

    /// <summary>POST /api/v1/auth/tokens</summary>
    IssueToken,

    /// <summary>GET /api/v1/auth/tokens</summary>
    ListOwnTokens,

    /// <summary>DELETE /api/v1/auth/tokens/{id}</summary>
    RevokeOwnToken,

    // ── Admin ─────────────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/admin/stats</summary>
    ReadAdminStats,

    /// <summary>POST /api/v1/admin/users, DELETE /api/v1/admin/users/{username}</summary>
    ManageUser,

    /// <summary>PUT /api/v1/admin/packages/{scope}/{name}/roles — admin-gated override assign.</summary>
    AdminAssignPackageRole,

    /// <summary>DELETE /api/v1/admin/packages/{scope}/{name}/roles — admin-gated override revoke.</summary>
    AdminRevokePackageRole,

    /// <summary>GET /api/v1/admin/audit-log</summary>
    ReadAuditLog,

    // ── Search ────────────────────────────────────────────────────────────────

    /// <summary>GET /api/v1/search</summary>
    Search
}
