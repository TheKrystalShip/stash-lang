namespace Stash.Registry.Services;

/// <summary>
/// Named constants for every audit-log <c>action</c> string written by
/// <see cref="AuditService"/> and the controllers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bounded domain — single source of truth.</b>  The set of valid audit action strings
/// is closed.  Both write sites (<see cref="AuditService"/> and the controllers) and the
/// read site (<c>AdminController.GetStats</c> activity queries) reference these constants
/// so that a rename propagates automatically and "same closed set duplicated across files"
/// is impossible.
/// </para>
/// <para>
/// Action strings <b>are</b> wire-exposed in two places:
/// <list type="bullet">
///   <item><description><c>AuditEntryResponse.Action</c> returned by <c>GET /api/v1/admin/audit-log</c>.</description></item>
///   <item><description><c>AuditLogQuery.action</c> filter on the same endpoint.</description></item>
/// </list>
/// These constants are server-internal for co-location reasons only.  A constant <b>NAME</b>
/// may be freely renamed (callers reference the symbol, not the string), but changing a
/// constant <b>VALUE</b> is a wire-breaking change and must never be done without a
/// coordinated migration.
/// </para>
/// </remarks>
public static class AuditActions
{
    // ── Package lifecycle ─────────────────────────────────────────────────────

    /// <summary>A new package was created by its first publish.</summary>
    public const string PackageCreate = "package.create";

    /// <summary>
    /// A new version was published onto an existing package.
    /// This is the real controller mutation audit action, written by
    /// <c>PackagesController.PublishPackage</c> and counted by
    /// <c>AdminController.GetStats</c> (publishesLast24h).  It is the
    /// authoritative wire contract value for publish operations.
    /// </summary>
    public const string PackagePublish = "package.publish";

    /// <summary>
    /// A version was unpublished (within the unpublish window).
    /// This is the real controller mutation audit action, written by
    /// <c>PackagesController.UnpublishVersion</c> and counted by
    /// <c>AdminController.GetStats</c> (unpublishesLast24h).  It is the
    /// authoritative wire contract value for unpublish operations.
    /// </summary>
    public const string PackageUnpublish = "package.unpublish";

    // ── Legacy helper values (test-only; no production caller) ───────────────

    /// <summary>
    /// Value logged by the vestigial <c>AuditService.LogPublishAsync</c> helper
    /// (test-only; no production caller).  Kept distinct from
    /// <see cref="PackagePublish"/> (<c>"package.publish"</c>), which is the real
    /// controller mutation audit wire contract.  Changing this value is a
    /// wire-breaking change for any consumer filtering by <c>action="publish"</c>.
    /// </summary>
    public const string Publish = "publish";

    /// <summary>
    /// Value logged by the vestigial <c>AuditService.LogUnpublishAsync</c> helper
    /// (test-only; no production caller).  Kept distinct from
    /// <see cref="PackageUnpublish"/> (<c>"package.unpublish"</c>), which is the
    /// real controller mutation audit wire contract.  Changing this value is a
    /// wire-breaking change for any consumer filtering by <c>action="unpublish"</c>.
    /// </summary>
    public const string Unpublish = "unpublish";

    /// <summary>A package was marked as deprecated.</summary>
    public const string PackageDeprecate = "package.deprecate";

    /// <summary>A package's deprecation was removed.</summary>
    public const string PackageUndeprecate = "package.undeprecate";

    // ── Version lifecycle ─────────────────────────────────────────────────────

    /// <summary>A specific version was marked as deprecated.</summary>
    public const string VersionDeprecate = "version.deprecate";

    /// <summary>A specific version's deprecation was removed.</summary>
    public const string VersionUndeprecate = "version.undeprecate";
}
