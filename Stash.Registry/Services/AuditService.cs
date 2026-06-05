using System;
using System.Threading.Tasks;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Services;

/// <summary>
/// Records security-relevant registry events in the audit log.
/// </summary>
/// <remarks>
/// <para>
/// <b>Audit scope (D19):</b>
/// <list type="bullet">
///   <item><description>Every state-mutating authorized action emits one entry with <c>decision="allow"</c>.</description></item>
///   <item><description>Every <em>authenticated</em> authorization denial emits one entry with <c>decision="deny"</c> and the typed <see cref="AuthzDenyReason"/>.</description></item>
///   <item><description>Anonymous public-read denials (404 / <see cref="AuthzDenyReason.VisibilityHidden"/>) are <b>excluded</b> — callers must check before calling.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class AuditService
{
    private readonly IRegistryDatabase _db;

    /// <summary>Initialises the service with the registry database.</summary>
    public AuditService(IRegistryDatabase db)
    {
        _db = db;
    }

    // ── PDP-aware mutation audit (P3) ─────────────────────────────────────────

    /// <summary>
    /// Records a successful authorized mutation.  Called after the PDP returns ALLOW
    /// and the mutation has succeeded.
    /// </summary>
    /// <param name="action">The action name, e.g. <c>"package.create"</c>, <c>"package.publish"</c>.</param>
    /// <param name="principalId">The username of the caller.</param>
    /// <param name="resource">The resource description (package name, scope, etc.).</param>
    /// <param name="ip">The caller's IP address, or <c>null</c>.</param>
    public async Task LogMutationAllowAsync(string action, string principalId, string resource, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = action,
            Package = resource,
            User = principalId,
            Decision = "allow",
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Records a successful authorized role mutation where a secondary target is involved.
    /// </summary>
    /// <param name="action">The action name, e.g. <c>"role.assign"</c>, <c>"role.revoke"</c>.</param>
    /// <param name="principalId">The username of the caller performing the action.</param>
    /// <param name="resource">The resource (package name).</param>
    /// <param name="target">The principal being assigned or revoked.</param>
    /// <param name="ip">The caller's IP address, or <c>null</c>.</param>
    public async Task LogRoleMutationAllowAsync(string action, string principalId, string resource, string target, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = action,
            Package = resource,
            User = principalId,
            Target = target,
            Decision = "allow",
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Records an authenticated authorization denial.
    /// </summary>
    /// <remarks>
    /// Must only be called when the principal is authenticated.  Anonymous denials
    /// (e.g. unauthenticated access to a private package returning 404) must NOT be
    /// recorded here — doing so would flood the audit table with noise.
    /// </remarks>
    /// <param name="action">The action that was denied.</param>
    /// <param name="principalId">The authenticated username.</param>
    /// <param name="resource">The resource that was the target of the attempted action.</param>
    /// <param name="reason">The typed deny reason from the PDP.</param>
    /// <param name="ip">The caller's IP address, or <c>null</c>.</param>
    public async Task LogAuthzDenyAsync(string action, string principalId, string resource, AuthzDenyReason reason, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = action,
            Package = resource,
            User = principalId,
            Decision = "deny",
            DenyReason = reason.ToString(),
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    // ── Legacy methods (retained for non-P3 paths still using them) ──────────

    public async Task LogPublishAsync(string package, string version, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = AuditActions.Publish,
            Package = package,
            Version = version,
            User = user,
            Decision = "allow",
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogUnpublishAsync(string package, string version, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = AuditActions.Unpublish,
            Package = package,
            Version = version,
            User = user,
            Decision = "allow",
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogRoleAssignAsync(string package, string user, string target, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "role.assign",
            Package = package,
            User = user,
            Target = target,
            Decision = "allow",
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogRoleRevokeAsync(string package, string user, string target, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "role.revoke",
            Package = package,
            User = user,
            Target = target,
            Decision = "allow",
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogUserCreateAsync(string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "user.create",
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogUserDisableAsync(string user, string target, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "user.disable",
            User = user,
            Target = target,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogTokenCreateAsync(string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "token.create",
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogTokenRevokeAsync(string user, string tokenId, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "token.revoke",
            User = user,
            Target = tokenId,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogTokenRefreshAsync(string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "token.refresh",
            User = user,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Records a token theft detection event where a consumed refresh token was reused.
    /// </summary>
    public async Task LogTokenTheftDetectedAsync(string user, string familyId, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "token_theft_detected",
            User = user,
            Target = familyId,
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogPackageDeprecateAsync(string package, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = AuditActions.PackageDeprecate,
            Package = package,
            User = user,
            Decision = "allow",
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogPackageUndeprecateAsync(string package, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = AuditActions.PackageUndeprecate,
            Package = package,
            User = user,
            Decision = "allow",
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogVersionDeprecateAsync(string package, string version, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = AuditActions.VersionDeprecate,
            Package = package,
            Version = version,
            User = user,
            Decision = "allow",
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogVersionUndeprecateAsync(string package, string version, string user, string? ip)
    {
        await _db.AddAuditEntryAsync(new AuditEntry
        {
            Action = AuditActions.VersionUndeprecate,
            Package = package,
            Version = version,
            User = user,
            Decision = "allow",
            Ip = ip,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task<SearchResult<AuditEntry>> GetAuditLogAsync(int page, int pageSize, string? packageName = null, string? action = null)
    {
        return await _db.GetAuditLogAsync(page, pageSize, packageName, action);
    }
}
