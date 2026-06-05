using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Stash.Registry.Configuration;

/// <summary>
/// Implements <see cref="IIpHasher"/> using the configured
/// <see cref="IpHandlingMode"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>HMAC secret persistence.</b>  When the mode is
/// <see cref="IpHandlingMode.Hashed"/>, the secret is loaded once from disk
/// at construction time.  If the file does not exist, a 32-byte random secret is
/// generated, written to <see cref="SecretFilePath"/>, and a warning is logged naming
/// the file.  The secret MUST persist across restarts so the same source IP always
/// produces the same hash — do NOT regenerate on every startup (unlike the JWT key).
/// </para>
/// <para>
/// <b>Truncation.</b>
/// IPv4: zeroes the last octet (effectively /24 — e.g. <c>192.168.1.0</c>).
/// IPv6: zeroes the last 64 bits / 8 bytes, keeping only the network prefix
///       (e.g. <c>2001:db8::</c> from <c>2001:db8::1</c>).
/// </para>
/// <para>
/// <b>Hashing.</b>
/// Returns the first 32 hex characters of <c>HMACSHA256(secret, address_string_utf8)</c>.
/// </para>
/// </remarks>
public sealed class IpHasher : IIpHasher
{
    /// <summary>
    /// Default secret file path sibling to the SQLite database file.
    /// </summary>
    public const string DefaultSecretFileName = "metrics-ip-secret.bin";

    private readonly IpHandlingMode _mode;
    private readonly byte[]? _hmacKey; // non-null only when _mode == Hashed

    /// <summary>
    /// The full path to the HMAC secret file that was loaded (or generated) during
    /// construction.  Set only when mode is <see cref="IpHandlingMode.Hashed"/>.
    /// </summary>
    public string? SecretFilePath { get; }

    /// <summary>
    /// Constructs an <see cref="IpHasher"/> from the supplied configuration.
    /// </summary>
    /// <param name="config">The metrics configuration section.</param>
    /// <param name="logger">Logger for startup warnings.</param>
    /// <param name="databasePath">
    /// Optional path to the SQLite database file.  When provided, the auto-generated
    /// secret is written as a sibling of the database directory rather than the
    /// application binary directory.  Ignored when
    /// <see cref="MetricsConfig.IpHashSecret"/> is set.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the <see cref="IpHandlingMode.Hashed"/> mode is active and
    /// <see cref="MetricsConfig.IpHashSecret"/> is not valid base-64.
    /// </exception>
    public IpHasher(MetricsConfig config, ILogger<IpHasher> logger, string? databasePath = null)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (logger is null) throw new ArgumentNullException(nameof(logger));

        _mode = config.IpMode;

        if (_mode != IpHandlingMode.Hashed)
            return;

        // ── Resolve the secret ────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(config.IpHashSecret))
        {
            // Operator-supplied base-64 secret.
            try
            {
                _hmacKey = Convert.FromBase64String(config.IpHashSecret);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Registry:Metrics:IpHashSecret is not valid base-64. " +
                    "Provide a valid base-64-encoded secret or remove the key to " +
                    "let the registry auto-generate one.", ex);
            }
        }
        else
        {
            // Auto-generate once and persist to disk so hashes stay stable across
            // registry restarts.  The file is placed in the database directory
            // (typically a persistent volume mount) rather than the binary output
            // directory, which may be ephemeral in container deployments.
            SecretFilePath = ResolveSecretFilePath(databasePath);
            _hmacKey = LoadOrCreateSecret(SecretFilePath, logger);
        }
    }

    /// <summary>
    /// Constructs an <see cref="IpHasher"/> with an explicit HMAC key (for testing).
    /// </summary>
    internal IpHasher(IpHandlingMode mode, byte[]? hmacKey = null)
    {
        _mode = mode;
        _hmacKey = hmacKey;
    }

    /// <inheritdoc/>
    public string? Apply(IPAddress? address)
    {
        if (address is null)
            return null;

        return _mode switch
        {
            IpHandlingMode.Raw => address.ToString(),
            IpHandlingMode.Truncated => Truncate(address),
            IpHandlingMode.Hashed => Hash(address),
            IpHandlingMode.Off => null,
            _ => null
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string Truncate(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // IPv4: zero the last octet → /24 prefix
            byte[] bytes = address.GetAddressBytes(); // always 4 bytes
            bytes[3] = 0;
            return new IPAddress(bytes).ToString();
        }
        else
        {
            // IPv6: zero the last 8 bytes → /64 prefix
            byte[] bytes = address.GetAddressBytes(); // always 16 bytes
            for (int i = 8; i < 16; i++)
                bytes[i] = 0;
            return new IPAddress(bytes).ToString();
        }
    }

    private string Hash(IPAddress address)
    {
        if (_hmacKey is null)
            throw new InvalidOperationException(
                "IpHasher is in Hashed mode but no HMAC key is loaded. " +
                "This indicates a bug in IpHasher construction.");

        byte[] raw = Encoding.UTF8.GetBytes(address.ToString());
        using var hmac = new HMACSHA256(_hmacKey);
        byte[] hash = hmac.ComputeHash(raw);
        // Return first 32 hex characters (16 bytes = 128 bits)
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    // ── Secret file helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Resolves the default secret file path.
    /// </summary>
    /// <param name="databasePath">
    /// Optional path to the SQLite database file.  When provided and the directory
    /// exists (or can be created), the secret is placed in the same directory so it
    /// lives on the same persistent volume as the data.  Falls back to the application
    /// binary directory when <paramref name="databasePath"/> is null/empty.
    /// </param>
    private static string ResolveSecretFilePath(string? databasePath)
    {
        if (!string.IsNullOrWhiteSpace(databasePath))
        {
            string? dbDir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(dbDir))
                return Path.Combine(dbDir, DefaultSecretFileName);
        }

        // Fallback: sibling of the application binary (appsettings.json directory).
        return Path.Combine(AppContext.BaseDirectory, DefaultSecretFileName);
    }

    /// <summary>
    /// Loads the HMAC secret from <paramref name="filePath"/>, or generates and persists
    /// a new one if the file does not exist.
    /// </summary>
    private static byte[] LoadOrCreateSecret(string filePath, ILogger logger)
    {
        if (File.Exists(filePath))
        {
            byte[] existing = File.ReadAllBytes(filePath);
            if (existing.Length >= 16)
                return existing;

            // File exists but is too short — regenerate (corrupt / truncated file).
            logger.LogWarning(
                "Metrics IP-hash secret file '{FilePath}' is too short ({Length} bytes, expected >= 16). " +
                "Regenerating a new secret.",
                filePath, existing.Length);
        }

        // Generate a new 32-byte random key.
        byte[] secret = RandomNumberGenerator.GetBytes(32);

        try
        {
            // Ensure the directory exists.
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(filePath, secret);

            logger.LogWarning(
                "No Metrics IP-hash secret configured. A new HMAC-SHA256 secret has been " +
                "auto-generated and written to '{FilePath}'. " +
                "To ensure hash consistency across deployments, back up this file or set " +
                "'Registry:Metrics:IpHashSecret' in appsettings.json.",
                filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to persist the auto-generated Metrics IP-hash secret to '{FilePath}'. " +
                "Hash correlation will NOT survive registry restarts. " +
                "Set 'Registry:Metrics:IpHashSecret' in appsettings.json to fix this.",
                filePath);
        }

        return secret;
    }
}
