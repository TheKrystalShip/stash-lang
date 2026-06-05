using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stash.Registry.Configuration;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Services;

/// <summary>
/// Computes the tamper-evidence hash for audit log entries.
/// </summary>
/// <remarks>
/// <para>
/// This class is the <b>single source of truth</b> for both the write path
/// (<see cref="AuditService"/>) and the verify endpoint (<c>AdminController.VerifyAuditLog</c>).
/// Two divergent serializers is the classic tamper-evidence bug; there is exactly one home.
/// </para>
/// <para>
/// <b>Chain rule:</b> <c>entryHash = H(CanonicalPayload(entry) || previousHash)</c>, where:
/// <list type="bullet">
///   <item><description><c>H</c> is HMAC-SHA256 when <see cref="AuditTamperEvidenceConfig.HashSecret"/>
///   is set, otherwise plain SHA-256.</description></item>
///   <item><description><c>previousHash</c> is the <see cref="AuditEntry.EntryHash"/> of the immediately
///   prior hashed entry (or <see cref="GenesisSentinel"/> for the first).</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Process-global lock.</b>  <see cref="WriteLock"/> serializes the "read last hash →
/// compute → insert" critical section when tamper-evidence is enabled.  <see cref="AuditService"/>
/// acquires it around every hashed append.  The lock is <c>static</c> so it is shared across all
/// DI-scoped <see cref="AuditService"/> instances in the same process.
/// </para>
/// <para>
/// Always registered in DI (even when disabled) to avoid nullable-type confusion.
/// <see cref="IsEnabled"/> signals callers when hashing should be skipped.
/// </para>
/// </remarks>
public sealed class AuditChainHasher
{
    /// <summary>
    /// The genesis sentinel written as the <c>previousHash</c> of the very first hashed entry
    /// in a run.  Using a fixed non-empty string distinguishes "genesis" from "pre-genesis null"
    /// and ensures the chain computation is always over a non-empty previous value.
    /// </summary>
    public const string GenesisSentinel = "genesis";

    /// <summary>
    /// Process-global async lock that serializes "read last hash → compute → insert".
    /// Acquired by <see cref="AuditService.AddEntryAsync"/> when tamper-evidence is enabled.
    /// Must be <c>static</c> because <see cref="AuditService"/> is <c>Scoped</c>; a per-instance
    /// lock would not prevent cross-request chain forks.
    /// </summary>
    public static readonly System.Threading.SemaphoreSlim WriteLock = new(1, 1);

    private readonly AuditTamperEvidenceConfig _config;

    /// <summary>
    /// Whether tamper-evidence hashing is enabled.  When <c>false</c>, <see cref="AuditService"/>
    /// skips the critical section and <c>AdminController.VerifyAuditLog</c> returns
    /// <c>enabled=false</c> without querying the chain.
    /// </summary>
    public bool IsEnabled => _config.Enabled;

    /// <summary>
    /// Initialises the hasher with the tamper-evidence configuration.
    /// </summary>
    public AuditChainHasher(AuditTamperEvidenceConfig config)
    {
        _config = config;
    }

    // ── Canonical payload ─────────────────────────────────────────────────────

    /// <summary>
    /// Produces the canonical UTF-8 JSON bytes over the content fields of
    /// <paramref name="entry"/> that are hashed in the chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fixed field order:</b>
    /// <c>action, package, version, user, target, ip, timestamp, decision, denyReason</c>.
    /// The <c>id</c> field is deliberately excluded (it is a surrogate key that may differ
    /// across databases) and the hash fields (<c>previousHash</c>/<c>entryHash</c>) are
    /// excluded (they depend on the payload).
    /// </para>
    /// <para>
    /// <b>Timestamp format:</b> ISO-8601 UTC with fixed precision
    /// (<c>"yyyy-MM-ddTHH:mm:ss.fffffffZ"</c>).  EF/SQLite round-trips a
    /// <see cref="DateTime"/> with <c>Kind=Unspecified</c> — the field is normalised to
    /// <c>Kind=Utc</c> before formatting so the stored string always ends in <c>Z</c>.
    /// </para>
    /// </remarks>
    /// <param name="entry">The audit entry to serialise.</param>
    /// <returns>Canonical UTF-8 JSON bytes.</returns>
    public static byte[] CanonicalPayload(AuditEntry entry)
    {
        // Normalise timestamp to Utc so the "Z" suffix is stable across EF round-trips.
        // DateTime.SpecifyKind is used (not ToUniversalTime) because SQLite reads back
        // Unspecified and ToUniversalTime would treat Unspecified as local, causing a shift.
        var ts = DateTime.SpecifyKind(entry.Timestamp, DateTimeKind.Utc);
        string tsStr = ts.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture);

        // Build a JsonObject with fixed field order using Utf8JsonWriter for determinism.
        using var ms = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("action",     entry.Action ?? "");
            WriteNullableString(writer, "package",    entry.Package);
            WriteNullableString(writer, "version",    entry.Version);
            WriteNullableString(writer, "user",       entry.User);
            WriteNullableString(writer, "target",     entry.Target);
            WriteNullableString(writer, "ip",         entry.Ip);
            writer.WriteString("timestamp",           tsStr);
            WriteNullableString(writer, "decision",   entry.Decision);
            WriteNullableString(writer, "denyReason", entry.DenyReason);
            writer.WriteEndObject();
        }
        return ms.ToArray();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value == null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }

    // ── Hash computation ──────────────────────────────────────────────────────

    /// <summary>
    /// Computes <c>entryHash = H(CanonicalPayload(entry) || previousHash)</c>.
    /// </summary>
    /// <param name="entry">The entry to hash (all content fields must already be set).</param>
    /// <param name="previousHash">
    /// The <see cref="AuditEntry.EntryHash"/> of the prior hashed entry, or
    /// <see cref="GenesisSentinel"/> for the first entry in a run.
    /// </param>
    /// <returns>Hex-encoded hash string.</returns>
    public string ComputeEntryHash(AuditEntry entry, string previousHash)
    {
        byte[] payload   = CanonicalPayload(entry);
        byte[] prevBytes = Encoding.UTF8.GetBytes(previousHash);

        // Concatenate payload || previousHash
        byte[] input = new byte[payload.Length + prevBytes.Length];
        Buffer.BlockCopy(payload,   0, input, 0,              payload.Length);
        Buffer.BlockCopy(prevBytes, 0, input, payload.Length, prevBytes.Length);

        if (!string.IsNullOrEmpty(_config.HashSecret))
        {
            // HMAC-SHA256 keyed with the operator-supplied base64 secret.
            byte[] key;
            try   { key = Convert.FromBase64String(_config.HashSecret); }
            catch { key = Encoding.UTF8.GetBytes(_config.HashSecret); }

            using var hmac = new HMACSHA256(key);
            return Convert.ToHexString(hmac.ComputeHash(input)).ToLowerInvariant();
        }
        else
        {
            // Plain SHA-256.
            return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        }
    }
}
