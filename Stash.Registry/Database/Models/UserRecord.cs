using System;

namespace Stash.Registry.Database.Models;

public sealed class UserRecord
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "user"; // "user" or "admin"
    public DateTime CreatedAt { get; set; }
}
