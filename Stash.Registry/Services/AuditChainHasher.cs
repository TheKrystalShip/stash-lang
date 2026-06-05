using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
    /// <exception cref="InvalidOperationException">
    /// Thrown at construction time when <paramref name="config"/> has
    /// <see cref="AuditTamperEvidenceConfig.Enabled"/> set to <c>true</c> and
    /// <see cref="AuditTamperEvidenceConfig.HashSecret"/> is a non-empty string
    /// that is not valid base64.  Because the hasher is registered as a singleton
    /// in DI via <c>Startup.ConfigureServices</c>, this failure surfaces at
    /// process startup — not on the first audit write — so an operator can see
    /// and fix the misconfiguration immediately.
    /// </exception>
    public AuditChainHasher(AuditTamperEvidenceConfig config)
    {
        if (config.Enabled && !string.IsNullOrEmpty(config.HashSecret))
        {
            try   { Convert.FromBase64String(config.HashSecret); }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Audit.TamperEvidence.HashSecret is not valid base64. " +
                    "Supply a base64-encoded key (e.g. the output of `openssl rand -base64 32`).",
                    ex);
            }
        }
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
            // The constructor has already validated that HashSecret is valid base64,
            // so this decode is known-safe and needs no try/catch.
            byte[] key = Convert.FromBase64String(_config.HashSecret);

            using var hmac = new HMACSHA256(key);
            return Convert.ToHexString(hmac.ComputeHash(input)).ToLowerInvariant();
        }
        else
        {
            // Plain SHA-256.
            return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        }
    }

    // ── Chain verification ────────────────────────────────────────────────────

    /// <summary>
    /// The result of walking the tamper-evidence chain, as returned by
    /// <see cref="WalkChainAsync"/>.
    /// </summary>
    public sealed record ChainWalkResult(bool Valid, int? FirstBrokenId, int? GenesisId, int CheckedCount);

    /// <summary>
    /// Walks the hashed-entry stream (id-ascending, hashed only) and returns the
    /// chain integrity result.  This is the <b>single source of truth</b> for the walk logic,
    /// called by both <c>AdminController.VerifyAuditLog</c> and the unit tests — there must
    /// be exactly one walker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first entry yielded by <paramref name="hashedEntries"/> is the <b>anchor</b>.  Its
    /// stored <c>PreviousHash</c> is trusted without verification (it may point to a deleted
    /// genesis after a retention sweep, or be the <see cref="GenesisSentinel"/>).  Only the
    /// content check (<c>recomputed == stored EntryHash</c>) is performed for the anchor.
    /// </para>
    /// <para>
    /// For all subsequent entries both the content check and the linkage check
    /// (<c>entry.PreviousHash == prior.EntryHash</c>) are performed.
    /// </para>
    /// <para>
    /// Pre-genesis entries (null-hash rows) must be excluded by the caller before passing to
    /// this method.  <see cref="IRegistryDatabase.StreamHashedAuditEntriesAsync"/> does this
    /// automatically.
    /// </para>
    /// <para>
    /// Memory usage is O(1) — only the prior entry's <c>EntryHash</c>, the genesis id, the
    /// first-broken id, and the running count are retained.  The stream is consumed once and
    /// is not buffered.
    /// </para>
    /// </remarks>
    /// <param name="hashedEntries">
    /// Lazily-streamed hashed audit entries, ordered by <c>id</c> ascending.  Must be
    /// non-null; may be empty (returns <c>CheckedCount=0, Valid=true</c>).
    /// </param>
    public async Task<ChainWalkResult> WalkChainAsync(IAsyncEnumerable<AuditEntry> hashedEntries)
    {
        int?   genesisId    = null;
        int?   firstBrokenId = null;
        int    checkedCount = 0;
        string? priorEntryHash = null; // EntryHash of the previous entry (null before first)

        await foreach (var entry in hashedEntries)
        {
            checkedCount++;

            if (genesisId == null)
            {
                // First entry — it is the anchor.  Trust its stored PreviousHash (window anchor).
                genesisId = entry.Id;
                string anchorPrev = entry.PreviousHash!;
                string recomputed = ComputeEntryHash(entry, anchorPrev);
                if (entry.EntryHash != recomputed && firstBrokenId == null)
                    firstBrokenId = entry.Id;
            }
            else
            {
                // Non-anchor: linkage check then content check.
                if (entry.PreviousHash != priorEntryHash && firstBrokenId == null)
                    firstBrokenId = entry.Id;

                string recomputed = ComputeEntryHash(entry, priorEntryHash!);
                if (entry.EntryHash != recomputed && firstBrokenId == null)
                    firstBrokenId = entry.Id;
            }

            priorEntryHash = entry.EntryHash;
        }

        if (checkedCount == 0)
            return new ChainWalkResult(Valid: true, FirstBrokenId: null, GenesisId: null, CheckedCount: 0);

        return new ChainWalkResult(
            Valid:          firstBrokenId == null,
            FirstBrokenId: firstBrokenId,
            GenesisId:     genesisId,
            CheckedCount:  checkedCount);
    }
}
