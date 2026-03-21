using System;

namespace Stash.Registry.Database.Models;

public sealed class AuditEntry
{
    public int Id { get; set; }
    public string Action { get; set; } = "";
    public string? Package { get; set; }
    public string? Version { get; set; }
    public string? User { get; set; }
    public string? Target { get; set; }
    public string? Ip { get; set; }
    public DateTime Timestamp { get; set; }
}
