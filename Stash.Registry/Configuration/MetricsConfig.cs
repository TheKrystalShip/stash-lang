using System;

namespace Stash.Registry.Configuration;

/// <summary>
/// Operator-configurable metrics settings, bound from <c>Registry:Metrics</c>
/// in <c>appsettings.json</c>.
/// </summary>
public sealed class MetricsConfig
{
    /// <summary>
    /// Whether metrics collection is enabled.  Defaults to <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How the caller's remote IP address is handled when persisting download events.
    /// Valid values: <c>raw</c>, <c>truncated</c>, <c>hashed</c>, <c>off</c>.
    /// Defaults to <see cref="IpHandlingMode.Hashed"/>.
    /// </summary>
    /// <remarks>
    /// The .NET <c>ConfigurationBinder</c> performs a case-insensitive
    /// <see cref="Enum.TryParse{T}"/> when binding this property.  An unrecognised
    /// string succeeds syntactically but produces a numeric 0 (the CLR default,
    /// which is <see cref="IpHandlingMode.Raw"/>).  To detect illegal values early,
    /// <see cref="MetricsConfigValidator.Validate"/> re-parses the raw string and
    /// throws if it does not match any named member.
    /// </remarks>
    public IpHandlingMode IpMode { get; set; } = IpHandlingMode.Hashed;

    /// <summary>
    /// Base-64-encoded HMAC-SHA256 secret used when <see cref="IpMode"/> is
    /// <see cref="IpHandlingMode.Hashed"/>.  If absent on startup the registry
    /// generates a 32-byte random secret, writes it to the secret file, and logs a
    /// warning naming the file written.
    /// </summary>
    public string? IpHashSecret { get; set; }

    /// <summary>Raw-event retention settings.</summary>
    public MetricsRawConfig Raw { get; set; } = new();

    /// <summary>Rollup interval settings.</summary>
    public MetricsRollupConfig Rollup { get; set; } = new();
}

/// <summary>
/// Raw download-event retention settings.
/// </summary>
public sealed class MetricsRawConfig
{
    /// <summary>
    /// How many days raw download events are retained before the nightly retention
    /// sweep deletes them.  <c>0</c> disables raw capture entirely.
    /// Defaults to <c>30</c>.
    /// </summary>
    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// Rollup interval settings.
/// </summary>
public sealed class MetricsRollupConfig
{
    /// <summary>
    /// How often (in minutes) the background service rolls up raw events into the
    /// hourly rollup buckets.  Defaults to <c>60</c>.
    /// </summary>
    public int IntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Validates a bound <see cref="MetricsConfig"/> at startup.
/// </summary>
public static class MetricsConfigValidator
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the configuration is invalid.
    /// </summary>
    /// <remarks>
    /// Validates:
    /// <list type="bullet">
    ///   <item>The raw <c>IpMode</c> string (from the raw configuration key) maps to a
    ///     known <see cref="IpHandlingMode"/> member — rejects unknown strings with a
    ///     clear message listing the legal values.</item>
    ///   <item><c>Raw.RetentionDays</c> is non-negative.</item>
    ///   <item><c>Rollup.IntervalMinutes</c> is positive.</item>
    /// </list>
    /// </remarks>
    /// <param name="config">The bound configuration to validate.</param>
    /// <param name="rawIpModeString">
    /// The raw string value of <c>Registry:Metrics:IpMode</c> as read directly from the
    /// configuration source, used to detect illegal values that the binder silently
    /// mapped to a default.  Pass <c>null</c> to skip this check (e.g. when the key
    /// was not present in the configuration source).
    /// </param>
    public static void Validate(MetricsConfig config, string? rawIpModeString)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));

        // Re-validate the raw IpMode string.  ConfigurationBinder calls Enum.TryParse,
        // which silently succeeds for an unknown string (assigning the numeric 0 value).
        // We reject that by checking the raw string explicitly.
        if (rawIpModeString is not null)
        {
            if (!Enum.TryParse<IpHandlingMode>(rawIpModeString, ignoreCase: true, out _))
            {
                throw new InvalidOperationException(
                    $"Invalid Registry:Metrics:IpMode value '{rawIpModeString}'. " +
                    $"Valid values are: raw, truncated, hashed, off.");
            }
        }

        if (config.Raw.RetentionDays < 0)
        {
            throw new InvalidOperationException(
                $"Invalid Registry:Metrics:Raw:RetentionDays value '{config.Raw.RetentionDays}': must be >= 0.");
        }

        if (config.Rollup.IntervalMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid Registry:Metrics:Rollup:IntervalMinutes value '{config.Rollup.IntervalMinutes}': must be a positive integer.");
        }
    }
}
