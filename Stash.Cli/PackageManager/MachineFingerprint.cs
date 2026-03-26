using System;
using System.Security.Cryptography;
using System.Text;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Generates a deterministic machine fingerprint based on the current hostname,
/// OS username, and platform. Used to bind refresh tokens to a specific machine
/// so that stolen tokens cannot be used from a different device.
/// </summary>
public static class MachineFingerprint
{
    /// <summary>
    /// Computes a SHA-256 hash of the machine's identifying characteristics.
    /// </summary>
    /// <returns>A lowercase hex string representing the machine fingerprint.</returns>
    public static string Generate()
    {
        string hostname = Environment.MachineName;
        string username = Environment.UserName;
        string platform = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : "linux";
        string raw = $"{hostname}:{username}:{platform}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
    }
}
