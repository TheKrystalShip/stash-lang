using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a daily download rollup bucket for a specific
/// package version.
/// </summary>
/// <remarks>
/// <para>
/// Daily rollup rows are permanent (never subject to retention sweeps). The background
/// rollup service aggregates raw <c>download_events</c> (closed-day buckets,
/// <c>ts &lt; currentDayStart</c>) directly into this table; daily rows are NOT
/// recomputed from hourly rows — both daily and hourly come from the same raw
/// closed-bucket projection.
/// </para>
/// <para>
/// The composite primary key is (<see cref="PackageName"/>, <see cref="Version"/>,
/// <see cref="BucketStart"/>). Column names use <c>snake_case</c>.
/// </para>
/// </remarks>
public sealed class DownloadRollupDailyRecord
{
    /// <summary>The fully-qualified package name (e.g. <c>@scope/name</c>), part of the composite primary key.</summary>
    public string PackageName { get; set; } = "";

    /// <summary>The exact semantic version string, part of the composite primary key.</summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// The UTC start timestamp of the one-day bucket (truncated to midnight UTC),
    /// part of the composite primary key.
    /// </summary>
    public DateTime BucketStart { get; set; }

    /// <summary>The total number of successful downloads in this bucket.</summary>
    public long Downloads { get; set; }

    /// <summary>The total bytes served across all downloads in this bucket.</summary>
    public long BytesServed { get; set; }
}
