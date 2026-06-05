using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Configuration;
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
    private readonly IIpHasher _ipHasher;
    private readonly AuditChainHasher _chainHasher;

    /// <summary>
    /// Initialises the service with the registry database, IP-handling pipeline, and the
    /// tamper-evidence chain hasher.  The hasher is always provided (never null) — its
    /// <see cref="AuditChainHasher.IsEnabled"/> property signals whether hashing is active.
    /// </summary>
    public AuditService(IRegistryDatabase db, IIpHasher ipHasher, AuditChainHasher chainHasher)
    {
        _db = db;
        _ipHasher = ipHasher;
        _chainHasher = chainHasher;
    }

    /// <summary>
    /// Convenience constructor for tests and simple scenarios where tamper-evidence is disabled.
    /// Uses a disabled <see cref="AuditChainHasher"/> (no hash computation).
    /// </summary>
    internal AuditService(IRegistryDatabase db, IIpHasher ipHasher)
        : this(db, ipHasher, new AuditChainHasher(new Configuration.AuditTamperEvidenceConfig { Enabled = false }))
    {
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Single chokepoint for every audit write.  Applies the configured
    /// <see cref="IIpHasher"/> transform to <paramref name="rawIp"/> and persists
    /// <paramref name="entry"/> with the resulting value in the <c>Ip</c> column.
    /// This is the one place the IP transform runs so a new audit event physically
    /// cannot forget it.
    /// </summary>
    /// <remarks>
    /// When tamper-evidence is enabled (<see cref="AuditChainHasher"/> is non-null), this
    /// method acquires the <see cref="AuditChainHasher.WriteLock"/> before reading the tail
    /// hash, computing <c>entryHash = H(CanonicalPayload || previousHash)</c>, and inserting
    /// — all under the lock.  Audit writes are durable before the HTTP response returns;
    /// the <see cref="System.Threading.Channels.Channel"/>-based fire-and-forget pattern used
    /// by download metrics is deliberately <b>not</b> copied here.
    /// </remarks>
    private async Task AddEntryAsync(AuditEntry entry, string? rawIp)
    {
        IPAddress.TryParse(rawIp, out var address);
        entry.Ip = _ipHasher.Apply(address);

        if (_chainHasher.IsEnabled)
        {
            // Serialized append: acquire the process-global lock so that no two concurrent
            // requests can interleave their "read last hash → compute → insert" critical sections.
            await AuditChainHasher.WriteLock.WaitAsync();
            try
            {
                string? latestHash = await _db.GetLatestHashedEntryHashAsync();
                string previousHash = latestHash ?? AuditChainHasher.GenesisSentinel;
                entry.PreviousHash = previousHash;
                entry.EntryHash    = _chainHasher.ComputeEntryHash(entry, previousHash);
                await _db.AddAuditEntryAsync(entry);
            }
            finally
            {
                AuditChainHasher.WriteLock.Release();
            }
        }
        else
        {
            await _db.AddAuditEntryAsync(entry);
        }
    }

    /// <summary>
    /// Transforms an operator-supplied raw IP string through the same <see cref="IIpHasher"/>
    /// used at write time so the filter matches stored (already-transformed) values.
    /// Returns <c>null</c> when the transform yields null (e.g. <c>IpMode=off</c>), which
    /// signals the caller to short-circuit to an empty result.
    /// </summary>
    private string? TransformQueryIp(string rawIp)
    {
        IPAddress.TryParse(rawIp, out var address);
        return _ipHasher.Apply(address);
    }

    // ── PDP-aware mutation audit (P3) ─────────────────────────────────────────

    /// <summary>
    /// Records a successful authorized mutation.  Called after the PDP returns ALLOW
    /// and the mutation has succeeded.
    /// </summary>
    /// <param name="action">The action name, e.g. <c>"package.create"</c>, <c>"package.publish"</c>.</param>
    /// <param name="principalId">The username of the caller.</param>
    /// <param name="resource">The resource description (package name, scope, etc.).</param>
    /// <param name="ip">The caller's raw IP address string, or <c>null</c>.  Transformed before storage.</param>
    public async Task LogMutationAllowAsync(string action, string principalId, string resource, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = action,
            Package = resource,
            User = principalId,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    /// <summary>
    /// Records a successful authorized role mutation where a secondary target is involved.
    /// </summary>
    /// <param name="action">The action name, e.g. <c>"role.assign"</c>, <c>"role.revoke"</c>.</param>
    /// <param name="principalId">The username of the caller performing the action.</param>
    /// <param name="resource">The resource (package name).</param>
    /// <param name="target">The principal being assigned or revoked.</param>
    /// <param name="ip">The caller's raw IP address string, or <c>null</c>.  Transformed before storage.</param>
    public async Task LogRoleMutationAllowAsync(string action, string principalId, string resource, string target, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = action,
            Package = resource,
            User = principalId,
            Target = target,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
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
    /// <param name="ip">The caller's raw IP address string, or <c>null</c>.  Transformed before storage.</param>
    public async Task LogAuthzDenyAsync(string action, string principalId, string resource, AuthzDenyReason reason, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = action,
            Package = resource,
            User = principalId,
            Decision = "deny",
            DenyReason = reason.ToString(),
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    // ── Legacy methods (retained for non-P3 paths still using them) ──────────

    public async Task LogPublishAsync(string package, string version, string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.Publish,
            Package = package,
            Version = version,
            User = user,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogUnpublishAsync(string package, string version, string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.Unpublish,
            Package = package,
            Version = version,
            User = user,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogRoleAssignAsync(string package, string user, string target, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.RoleAssign,
            Package = package,
            User = user,
            Target = target,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogRoleRevokeAsync(string package, string user, string target, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.RoleRevoke,
            Package = package,
            User = user,
            Target = target,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogUserCreateAsync(string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.UserCreate,
            User = user,
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogUserDisableAsync(string user, string target, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.UserDisable,
            User = user,
            Target = target,
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogTokenCreateAsync(string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.TokenCreate,
            User = user,
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogTokenRevokeAsync(string user, string tokenId, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.TokenRevoke,
            User = user,
            Target = tokenId,
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogTokenRefreshAsync(string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.TokenRefresh,
            User = user,
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    /// <summary>
    /// Records a token theft detection event where a consumed refresh token was reused.
    /// </summary>
    public async Task LogTokenTheftDetectedAsync(string user, string familyId, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.TokenTheftDetected,
            User = user,
            Target = familyId,
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogPackageDeprecateAsync(string package, string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.PackageDeprecate,
            Package = package,
            User = user,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogPackageUndeprecateAsync(string package, string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.PackageUndeprecate,
            Package = package,
            User = user,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogVersionDeprecateAsync(string package, string version, string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.VersionDeprecate,
            Package = package,
            Version = version,
            User = user,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    public async Task LogVersionUndeprecateAsync(string package, string version, string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.VersionUndeprecate,
            Package = package,
            Version = version,
            User = user,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    // ── Authentication event helpers ──────────────────────────────────────────

    /// <summary>
    /// Records a successful login by <paramref name="user"/>.
    /// </summary>
    /// <param name="user">The username that authenticated.</param>
    /// <param name="ip">The caller's raw IP address string, or <c>null</c>.  Transformed before storage.</param>
    public async Task LogAuthLoginSuccessAsync(string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.AuthLoginSuccess,
            User = user,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    /// <summary>
    /// Records a failed login attempt (bad credentials).
    /// </summary>
    /// <param name="user">The username that was attempted.</param>
    /// <param name="ip">The caller's raw IP address string, or <c>null</c>.  Transformed before storage.</param>
    public async Task LogAuthLoginFailureAsync(string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.AuthLoginFailure,
            User = user,
            Decision = "deny",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    /// <summary>
    /// Records a failed token refresh attempt (invalid, expired, mismatched, or consumed token).
    /// </summary>
    /// <param name="user">The username associated with the token, or <c>null</c> if the token
    /// could not be validated (e.g. invalid JWT signature — the principal is unknown).</param>
    /// <param name="ip">The caller's raw IP address string, or <c>null</c>.  Transformed before storage.</param>
    public async Task LogAuthRefreshFailureAsync(string? user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.AuthRefreshFailure,
            User = user,
            Decision = "deny",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    /// <summary>
    /// Records a successful self-service registration.  Written alongside
    /// <see cref="LogUserCreateAsync"/> — both are intentional (the user-creation record
    /// and the authentication registration event are distinct; this is not a double-write bug).
    /// </summary>
    /// <param name="user">The newly registered username.</param>
    /// <param name="ip">The caller's raw IP address string, or <c>null</c>.  Transformed before storage.</param>
    public async Task LogAuthRegisterAsync(string user, string? ip)
    {
        await AddEntryAsync(new AuditEntry
        {
            Action = AuditActions.AuthRegister,
            User = user,
            Decision = "allow",
            Timestamp = DateTime.UtcNow
        }, ip);
    }

    /// <summary>
    /// Streams the full filtered audit log without pagination.
    /// Shares the same filter semantics as <see cref="GetAuditLogAsync"/> (logical AND,
    /// IP routed through <see cref="IIpHasher"/>, <c>IpMode=off</c> short-circuit) but
    /// yields all matching entries via successive page fetches so that a large log does
    /// not buffer entirely in memory.
    /// </summary>
    /// <param name="packageName">Optional exact-match package name filter.</param>
    /// <param name="action">Optional exact-match action filter.</param>
    /// <param name="user">Optional exact-match user filter.</param>
    /// <param name="target">Optional exact-match target filter.</param>
    /// <param name="version">Optional exact-match version filter.</param>
    /// <param name="ip">Optional raw IP filter; transformed through <see cref="IIpHasher"/> before matching.</param>
    /// <param name="from">Optional inclusive UTC lower-bound on timestamp.</param>
    /// <param name="to">Optional inclusive UTC upper-bound on timestamp.</param>
    /// <returns>
    /// An async sequence of <see cref="AuditEntry"/> objects in descending timestamp order, or
    /// an empty sequence when <c>IpMode=off</c> and an <paramref name="ip"/> filter is supplied.
    /// </returns>
    public async IAsyncEnumerable<AuditEntry> StreamAuditLogAsync(
        string? packageName = null, string? action = null,
        string? user = null, string? target = null, string? version = null,
        string? ip = null, DateTime? from = null, DateTime? to = null)
    {
        string? transformedIp = null;
        if (ip != null)
        {
            transformedIp = TransformQueryIp(ip);
            if (transformedIp == null)
                yield break;
        }

        const int pageSize = 200;
        int page = 1;
        while (true)
        {
            var result = await _db.GetAuditLogAsync(page, pageSize, packageName, action, user, target, version, transformedIp, from, to);
            foreach (var entry in result.Items)
                yield return entry;
            if (result.Items.Count < pageSize)
                break;
            page++;
        }
    }

    /// <summary>
    /// Returns a paginated, filtered view of the audit log.  All filter parameters are
    /// combined with logical AND; omitted (null) parameters are inert.
    /// The <paramref name="ip"/> filter is routed through the same <see cref="IIpHasher"/>
    /// used at write time so the operator can supply a raw IP regardless of the configured mode.
    /// </summary>
    public async Task<SearchResult<AuditEntry>> GetAuditLogAsync(
        int page, int pageSize,
        string? packageName = null, string? action = null,
        string? user = null, string? target = null, string? version = null,
        string? ip = null, DateTime? from = null, DateTime? to = null)
    {
        // Transform the operator-supplied IP the same way the write path does so the
        // filter matches the stored (already-transformed) value.
        string? transformedIp = null;
        if (ip != null)
        {
            transformedIp = TransformQueryIp(ip);
            // IpMode=off: Apply() returns null for any input → stored column is always null
            // → WHERE Ip == null would match ALL off-mode rows (wrong).
            // Short-circuit to empty result instead.
            if (transformedIp == null)
                return new SearchResult<AuditEntry> { Items = [], TotalCount = 0 };
        }

        return await _db.GetAuditLogAsync(page, pageSize, packageName, action, user, target, version, transformedIp, from, to);
    }
}
