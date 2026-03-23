using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a published package in the registry.
/// </summary>
/// <remarks>
/// The package name serves as the primary key. Related versions are stored in
/// <see cref="VersionRecord"/> and linked via the <c>package_name</c> foreign key.
/// Column names use <c>snake_case</c> in the database. The <see cref="Keywords"/>
/// property stores a JSON array serialised as a plain string.
/// </remarks>
public sealed class PackageRecord
{
    /// <summary>The unique package name (primary key, mapped to <c>name</c> column).</summary>
    public string Name { get; set; } = "";

    /// <summary>A short human-readable description of the package, or <c>null</c> if not provided.</summary>
    public string? Description { get; set; }

    /// <summary>The SPDX license identifier for the package, or <c>null</c> if not specified.</summary>
    public string? License { get; set; }

    /// <summary>The source repository URL, or <c>null</c> if not specified.</summary>
    public string? Repository { get; set; }

    /// <summary>The raw readme text, or <c>null</c> if the package has no readme.</summary>
    public string? Readme { get; set; }

    /// <summary>
    /// A JSON-serialised array of keyword strings, or <c>null</c> if no keywords were supplied.
    /// Deserialise with <see cref="System.Text.Json.JsonSerializer"/> to obtain a
    /// <c>List&lt;string&gt;</c>.
    /// </summary>
    public string? Keywords { get; set; } // JSON array stored as string

    /// <summary>The version string currently tagged as <c>latest</c> (required).</summary>
    public string Latest { get; set; } = "";

    /// <summary>The UTC timestamp at which the package was first published.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>The UTC timestamp of the most recent metadata or version update.</summary>
    public DateTime UpdatedAt { get; set; }
}
