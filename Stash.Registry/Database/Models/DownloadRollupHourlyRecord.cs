using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing an hourly download rollup bucket for a specific
/// package version.
/// </summary>
/// <remarks>
/// <para>
/// Hourly rollup rows are permanent (never subject to retention sweeps). The background
/// rollup service (<c>MetricsBackgroundService</c>, landed in M4) aggregates raw
/// <see cref="DownloadEventRecord"/> rows into this table at the configured interval.
/// </para>
/// <para>
/// The composite primary key is (<see cref="PackageName"/>, <see cref="Version"/>,
/// <see cref="BucketStart"/>). Column names use <c>snake_case</c>.
/// </para>
/// </remarks>
public sealed class DownloadRollupHourlyRecord
{
    /// <summary>The fully-qualified package name (e.g. <c>@scope/name</c>), part of the composite primary key.</summary>
    public string PackageName { get; set; } = "";

    /// <summary>The exact semantic version string, part of the composite primary key.</summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// The UTC start timestamp of the one-hour bucket (truncated to the hour),
    /// part of the composite primary key.
    /// </summary>
    public DateTime BucketStart { get; set; }

    /// <summary>The total number of successful downloads in this bucket.</summary>
    public long Downloads { get; set; }

    /// <summary>The total bytes served across all downloads in this bucket.</summary>
    public long BytesServed { get; set; }
}
