namespace Stash.Registry.Configuration;

/// <summary>
/// Operator-configurable IP-handling mode for download-event recording.
/// Controls how the caller's remote IP address is stored in telemetry events.
/// </summary>
/// <remarks>
/// <para>
/// This is the single source of truth for the IP-handling closed set.  It is a
/// server-internal type — it lives in <c>Stash.Registry/Configuration/</c>, NOT in
/// <c>Stash.Registry.Contracts</c> (which is wire-only).  The name is
/// <c>IpHandlingMode</c> (not <c>MetricsIpMode</c>) so that audit-log-v2 can reuse
/// it without rename.
/// </para>
/// <para>
/// The enum is bound from <c>appsettings.json</c> via the .NET
/// <c>ConfigurationBinder</c>.  An illegal string value (one that does not match any
/// member, case-insensitively) causes <see cref="MetricsConfigValidator.Validate"/> to
/// throw <see cref="InvalidOperationException"/> during startup, halting the process
/// with a clear message.
/// </para>
/// </remarks>
public enum IpHandlingMode
{
    /// <summary>
    /// Store the raw IP string verbatim
    /// (<c>HttpContext.Connection.RemoteIpAddress.ToString()</c>).
    /// </summary>
    Raw,

    /// <summary>
    /// Truncate to the network prefix: /24 for IPv4, /64 for IPv6.
    /// Retains geographic locality while discarding the host portion.
    /// </summary>
    Truncated,

    /// <summary>
    /// Store a 32-character hex HMAC-SHA256 of the raw IP string.
    /// The HMAC secret persists across restarts so the same source IP always
    /// produces the same hash.  This is the default.
    /// </summary>
    Hashed,

    /// <summary>
    /// Do not record the IP address at all; store <c>NULL</c>.
    /// </summary>
    Off,
}
