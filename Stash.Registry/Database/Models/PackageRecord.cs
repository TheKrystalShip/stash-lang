using System;

namespace Stash.Registry.Database.Models;

public sealed class PackageRecord
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? License { get; set; }
    public string? Repository { get; set; }
    public string? Readme { get; set; }
    public string? Keywords { get; set; } // JSON array stored as string
    public string Latest { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
