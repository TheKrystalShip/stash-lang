namespace Stash.Registry.Configuration;

/// <summary>
/// Operator-configurable audit-log settings, bound from <c>Registry:Audit</c>
/// in <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// This is a <b>separate</b> knob from <see cref="MetricsConfig"/> and its
/// <c>Raw.RetentionDays</c> property.  Two independent sweeps; two independent
/// defaults. <c>Audit.RetentionDays = 0</c> means "never delete" — the safe
/// default for a compliance log.
/// </remarks>
public sealed class AuditConfig
{
    /// <summary>
    /// How many days audit entries are retained before the nightly retention sweep
    /// deletes them.  Defaults to <c>0</c>.
    /// A value of <c>0</c> (or less) disables the nightly sweep entirely — audit
    /// entries accumulate indefinitely.  Set a positive value (e.g. <c>365</c>) to
    /// enable automatic pruning.
    /// </summary>
    /// <remarks>
    /// This is intentionally separate from <see cref="MetricsConfig.Raw"/>
    /// <c>.RetentionDays</c>.  Compliance logs and raw download telemetry have
    /// different operator retention obligations; conflating them would force a
    /// single knob to satisfy two independent regulatory requirements.
    /// </remarks>
    public int RetentionDays { get; set; } = 0;

    /// <summary>Optional tamper-evidence configuration (A6 behavior — shape defined here).</summary>
    public AuditTamperEvidenceConfig TamperEvidence { get; set; } = new();
}

/// <summary>
/// Optional hash-chain tamper-evidence settings for the audit log.
/// </summary>
/// <remarks>
/// This sub-section defines the <b>config shape</b> for tamper-evidence.
/// The write-path behavior (computing <c>entryHash</c>/<c>previousHash</c>) and
/// the verify endpoint are wired in A6.  A5 only reads <see cref="AuditConfig.RetentionDays"/>.
/// </remarks>
public sealed class AuditTamperEvidenceConfig
{
    /// <summary>
    /// Whether the hash-chain tamper-evidence feature is enabled.
    /// Defaults to <c>false</c>; opt-in.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Optional base-64-encoded HMAC secret used as the hash key.
    /// When <c>null</c> or empty, plain SHA-256 over the canonical payload is used.
    /// Relevant only when <see cref="Enabled"/> is <c>true</c>; ignored otherwise.
    /// </summary>
    public string? HashSecret { get; set; }
}
