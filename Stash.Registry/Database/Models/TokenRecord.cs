using System;

namespace Stash.Registry.Database.Models;

public sealed class TokenRecord
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string TokenHash { get; set; } = ""; // SHA-256 of the actual token
    public string Scope { get; set; } = "publish"; // "read", "publish", "admin"
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? Description { get; set; }
}
