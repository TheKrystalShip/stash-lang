using System;

namespace Stash.Registry.Database.Models;

public sealed class VersionRecord
{
    public string PackageName { get; set; } = "";
    public string Version { get; set; } = "";
    public string? StashVersion { get; set; }
    public string? Dependencies { get; set; } // JSON object stored as string
    public string Integrity { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public string PublishedBy { get; set; } = "";
}
