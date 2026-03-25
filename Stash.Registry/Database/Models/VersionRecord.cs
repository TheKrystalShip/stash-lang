using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a single published version of a package.
/// </summary>
/// <remarks>
/// The composite primary key is (<see cref="PackageName"/>, <see cref="Version"/>).
/// A version record is created for every successful publish operation and removed
/// when the version is unpublished. The <see cref="Dependencies"/> property stores
/// a JSON object serialised as a plain string. Column names use <c>snake_case</c>.
/// </remarks>
public sealed class VersionRecord
{
    /// <summary>The name of the owning package (part of the composite primary key, foreign key to <see cref="PackageRecord.Name"/>).</summary>
    public string PackageName { get; set; } = "";

    /// <summary>The exact semantic version string (part of the composite primary key).</summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// The minimum Stash runtime version required by this package version, or <c>null</c>
    /// if no constraint was declared.
    /// </summary>
    public string? StashVersion { get; set; }

    /// <summary>
    /// A JSON-serialised object mapping dependency names to version constraints, or
    /// <c>null</c> if there are no dependencies. Deserialise with
    /// <see cref="System.Text.Json.JsonSerializer"/> to obtain a
    /// <c>Dictionary&lt;string, object&gt;</c>.
    /// </summary>
    public string? Dependencies { get; set; } // JSON object stored as string

    /// <summary>The SHA-256 integrity hash of the package tarball (required).</summary>
    public string Integrity { get; set; } = "";

    /// <summary>The UTC timestamp at which this version was published.</summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>The username of the user who published this version (required).</summary>
    public string PublishedBy { get; set; } = "";

    /// <summary>Whether this version has been deprecated.</summary>
    public bool Deprecated { get; set; }

    /// <summary>A human-readable deprecation message, or <c>null</c> if not deprecated.</summary>
    public string? DeprecationMessage { get; set; }

    /// <summary>The username of the user who deprecated this version, or <c>null</c> if not deprecated.</summary>
    public string? DeprecatedBy { get; set; }
}
