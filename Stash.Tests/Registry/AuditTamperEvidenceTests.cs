using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// End-to-end unit tests for the tamper-evidence hash chain (A6 acceptance criteria).
/// Covers:
/// <list type="bullet">
///   <item><description>Chain valid after N hashed entries.</description></item>
///   <item><description>Tampered entry detected (valid=false, firstBrokenId).</description></item>
///   <item><description>disabled→enabled mid-stream genesis handling.</description></item>
///   <item><description>Retention x tamper — verify still valid after genesis deletion.</description></item>
///   <item><description>Canonical payload is deterministic and round-trips across EF/SQLite.</description></item>
///   <item><description><see cref="AuditChainHasher.CanonicalPayload"/> stability — same fields, same bytes.</description></item>
///   <item><description>HMAC-SHA256 vs plain SHA-256 toggling.</description></item>
///   <item><description>New columns round-trip through EF.</description></item>
/// </list>
/// </summary>
public sealed class AuditTamperEvidenceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly RegistryDbContext _dbContext;
    private readonly StashRegistryDatabase _db;

    public AuditTamperEvidenceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(_conn)
            .Options;
        _dbContext = new RegistryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _db = new StashRegistryDatabase(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _conn.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static AuditChainHasher BuildHasher(string? secret = null)
        => new(new AuditTamperEvidenceConfig { Enabled = true, HashSecret = secret });

    /// <summary>A disabled (no-op) hasher for tests that write pre-genesis entries.</summary>
    private static AuditChainHasher DisabledHasher()
        => new(new AuditTamperEvidenceConfig { Enabled = false });

    private static IIpHasher RawIpHasher()
        => new IpHasher(IpHandlingMode.Raw);

    private AuditService BuildAuditService(AuditChainHasher? hasher = null)
        => new(_db, RawIpHasher(), hasher ?? DisabledHasher());

    /// <summary>
    /// Walks the hashed entries in the DB using the provided <paramref name="hasher"/> and
    /// returns (valid, firstBrokenId, genesisId, checkedCount) — mirroring the verify endpoint logic.
    /// </summary>
    private async Task<(bool valid, int? firstBrokenId, int? genesisId, int checkedCount)>
        VerifyChainAsync(AuditChainHasher hasher)
    {
        var entries = await _db.GetAllHashedAuditEntriesAsync();
        if (entries.Count == 0)
            return (true, null, null, 0);

        int? firstBrokenId = null;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string expectedPreviousHash = i == 0
                ? entry.PreviousHash!
                : entries[i - 1].EntryHash!;

            string recomputed = hasher.ComputeEntryHash(entry, expectedPreviousHash);

            if (entry.EntryHash != recomputed)
            {
                firstBrokenId = entry.Id;
                break;
            }

            if (i > 0 && entry.PreviousHash != entries[i - 1].EntryHash)
            {
                firstBrokenId = entry.Id;
                break;
            }
        }

        return (firstBrokenId == null, firstBrokenId, entries[0].Id, entries.Count);
    }

    private static async Task<AuditEntry> WriteEntry(AuditService audit, string action = "package.publish",
        string package = "@a/foo", string user = "alice", string? ip = null)
    {
        // We use LogPublishAsync as a convenient proxy — it calls the single AddEntryAsync chokepoint.
        await audit.LogPublishAsync(package, "1.0.0", user, ip ?? "127.0.0.1");
        return null!; // caller queries DB directly
    }

    // ── AuditChainHasher.CanonicalPayload tests ─────────────────────────────

    [Fact]
    public void CanonicalPayload_Deterministic_SameEntryTwice()
    {
        var entry = new AuditEntry
        {
            Action    = "package.publish",
            Package   = "@a/foo",
            Version   = "1.0.0",
            User      = "alice",
            Target    = null,
            Ip        = "127.0.0.1",
            Timestamp = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc),
            Decision  = "allow",
            DenyReason = null,
        };

        byte[] b1 = AuditChainHasher.CanonicalPayload(entry);
        byte[] b2 = AuditChainHasher.CanonicalPayload(entry);

        Assert.Equal(b1, b2);
    }

    [Fact]
    public void CanonicalPayload_ExcludesIdAndHashFields()
    {
        var entry = new AuditEntry
        {
            Id         = 42,
            Action     = "auth.login.success",
            Timestamp  = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
            Decision   = "allow",
            // Hash fields — must not affect the payload
            PreviousHash = "genesis",
            EntryHash    = "somehash",
        };

        // Create a copy with different Id and hash fields but same content fields.
        var entryCopy = new AuditEntry
        {
            Id           = 99,
            Action       = entry.Action,
            Timestamp    = entry.Timestamp,
            Decision     = entry.Decision,
            PreviousHash = "different",
            EntryHash    = "anotherhash",
        };

        byte[] b1 = AuditChainHasher.CanonicalPayload(entry);
        byte[] b2 = AuditChainHasher.CanonicalPayload(entryCopy);

        Assert.Equal(b1, b2);
    }

    [Fact]
    public void CanonicalPayload_TimestampNormalisedToUtc()
    {
        var ts = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

        var entryUtc = new AuditEntry { Action = "x", Timestamp = ts };
        var entryUnspecified = new AuditEntry
        {
            Action    = "x",
            // SQLite EF reads back Unspecified kind — SpecifyKind normalises it.
            Timestamp = DateTime.SpecifyKind(ts, DateTimeKind.Unspecified),
        };

        byte[] bUtc = AuditChainHasher.CanonicalPayload(entryUtc);
        byte[] bUnspecified = AuditChainHasher.CanonicalPayload(entryUnspecified);

        Assert.Equal(bUtc, bUnspecified);
    }

    [Fact]
    public void CanonicalPayload_FixedFieldOrder()
    {
        // Verify the JSON contains the expected keys in the right order by checking the
        // raw UTF-8 bytes contain the key sequence.
        var entry = new AuditEntry
        {
            Action    = "a",
            Package   = "b",
            Version   = "c",
            User      = "d",
            Target    = "e",
            Ip        = "f",
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Decision  = "g",
            DenyReason = "h",
        };

        string json = System.Text.Encoding.UTF8.GetString(AuditChainHasher.CanonicalPayload(entry));

        // Keys must appear in this fixed order.
        string[] expectedKeys = ["action", "package", "version", "user", "target", "ip", "timestamp", "decision", "denyReason"];
        int lastPos = -1;
        foreach (var key in expectedKeys)
        {
            int pos = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            Assert.True(pos > lastPos,
                $"Key '{key}' not found in expected order. JSON: {json}");
            lastPos = pos;
        }

        // id and hash fields must NOT appear.
        Assert.DoesNotContain("\"id\"", json);
        Assert.DoesNotContain("\"previousHash\"", json);
        Assert.DoesNotContain("\"entryHash\"", json);
    }

    // ── New columns round-trip ─────────────────────────────────────────────────

    [Fact]
    public async Task AuditEntry_PreviousHashAndEntryHash_RoundTripThroughEF()
    {
        var entry = new AuditEntry
        {
            Action       = "package.publish",
            Package      = "@a/foo",
            User         = "alice",
            Decision     = "allow",
            Timestamp    = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc),
            PreviousHash = "genesis",
            EntryHash    = "abc123def456",
        };

        await _db.AddAuditEntryAsync(entry);

        var loaded = await _dbContext.AuditLog.AsNoTracking().FirstOrDefaultAsync();
        Assert.NotNull(loaded);
        Assert.Equal("genesis", loaded.PreviousHash);
        Assert.Equal("abc123def456", loaded.EntryHash);
    }

    [Fact]
    public async Task AuditEntry_NullHashFields_StoredAndReadBack()
    {
        var entry = new AuditEntry
        {
            Action    = "auth.login.success",
            User      = "bob",
            Timestamp = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
        };

        await _db.AddAuditEntryAsync(entry);

        var loaded = await _dbContext.AuditLog.AsNoTracking().FirstOrDefaultAsync();
        Assert.NotNull(loaded);
        Assert.Null(loaded.PreviousHash);
        Assert.Null(loaded.EntryHash);
    }

    // ── Chain valid after N hashed entries ────────────────────────────────────

    [Fact]
    public async Task TamperEvidence_FiveEntries_VerifiesValid()
    {
        var hasher = BuildHasher();
        var audit  = BuildAuditService(hasher);

        for (int i = 0; i < 5; i++)
            await audit.LogPublishAsync("@a/foo", $"1.0.{i}", "alice", "127.0.0.1");

        var (valid, firstBrokenId, genesisId, checkedCount) = await VerifyChainAsync(hasher);

        Assert.True(valid,        "Chain with 5 entries should be valid");
        Assert.Null(firstBrokenId);
        Assert.NotNull(genesisId);
        Assert.Equal(5, checkedCount);

        // Every entry must have non-null hashes.
        var all = await _dbContext.AuditLog.AsNoTracking().ToListAsync();
        Assert.All(all, e => Assert.NotNull(e.EntryHash));
        Assert.All(all, e => Assert.NotNull(e.PreviousHash));
    }

    [Fact]
    public async Task TamperEvidence_FiveEntries_FirstEntryHasGenesisSentinel()
    {
        var hasher = BuildHasher();
        var audit  = BuildAuditService(hasher);

        for (int i = 0; i < 5; i++)
            await audit.LogPublishAsync("@a/foo", $"1.0.{i}", "alice", null);

        var entries = await _dbContext.AuditLog.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(AuditChainHasher.GenesisSentinel, entries[0].PreviousHash);
    }

    [Fact]
    public async Task TamperEvidence_EntryChaining_EachPreviousHashMatchesPriorEntryHash()
    {
        var hasher = BuildHasher();
        var audit  = BuildAuditService(hasher);

        for (int i = 0; i < 4; i++)
            await audit.LogPublishAsync("@a/foo", $"1.0.{i}", "alice", null);

        var entries = await _dbContext.AuditLog.AsNoTracking().OrderBy(e => e.Id).ToListAsync();

        for (int i = 1; i < entries.Count; i++)
            Assert.Equal(entries[i - 1].EntryHash, entries[i].PreviousHash);
    }

    // ── Tamper detection ──────────────────────────────────────────────────────

    [Fact]
    public async Task TamperEvidence_TamperedUserField_DetectsBreak()
    {
        var hasher = BuildHasher();
        var audit  = BuildAuditService(hasher);

        // Write 5 entries.
        for (int i = 0; i < 5; i++)
            await audit.LogPublishAsync("@a/foo", $"1.0.{i}", "alice", null);

        // Identify the 3rd entry and mutate its User field directly (DB-level tampering).
        var entries = await _dbContext.AuditLog.OrderBy(e => e.Id).ToListAsync();
        var target  = entries[2]; // 3rd entry
        int targetId = target.Id;

        target.User = "mallory"; // tamper
        await _dbContext.SaveChangesAsync();

        // Reload in a clean no-tracking query.
        var (valid, firstBrokenId, genesisId, checkedCount) = await VerifyChainAsync(hasher);

        Assert.False(valid, "Chain should be broken after tampering");
        Assert.Equal(targetId, firstBrokenId);
        Assert.Equal(5, checkedCount);
    }

    [Fact]
    public async Task TamperEvidence_TamperedAction_DetectsBreak()
    {
        var hasher = BuildHasher();
        var audit  = BuildAuditService(hasher);

        for (int i = 0; i < 3; i++)
            await audit.LogPublishAsync("@a/foo", "1.0.0", $"user{i}", null);

        // Mutate the action of the first entry.
        var entries = await _dbContext.AuditLog.OrderBy(e => e.Id).ToListAsync();
        entries[0].Action = "malicious.action";
        await _dbContext.SaveChangesAsync();

        var (valid, firstBrokenId, _, _) = await VerifyChainAsync(hasher);

        Assert.False(valid);
        Assert.Equal(entries[0].Id, firstBrokenId);
    }

    // ── HMAC-SHA256 vs plain SHA-256 ──────────────────────────────────────────

    [Fact]
    public async Task TamperEvidence_WithHmacSecret_VerifiesValidChain()
    {
        string secret = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("my-test-secret-key"));
        var hasher = BuildHasher(secret);
        var audit  = BuildAuditService(hasher);

        for (int i = 0; i < 3; i++)
            await audit.LogPublishAsync("@a/foo", $"1.0.{i}", "alice", null);

        var (valid, firstBrokenId, _, checkedCount) = await VerifyChainAsync(hasher);

        Assert.True(valid);
        Assert.Null(firstBrokenId);
        Assert.Equal(3, checkedCount);
    }

    [Fact]
    public void TamperEvidence_HmacAndPlainSha256_ProduceDifferentHashes()
    {
        string secret = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("secret"));
        var hmacHasher  = BuildHasher(secret);
        var plainHasher = BuildHasher(null);

        var entry = new AuditEntry
        {
            Action    = "package.publish",
            Package   = "@a/foo",
            User      = "alice",
            Timestamp = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc),
            Decision  = "allow",
        };

        string hmacHash  = hmacHasher.ComputeEntryHash(entry, AuditChainHasher.GenesisSentinel);
        string plainHash = plainHasher.ComputeEntryHash(entry, AuditChainHasher.GenesisSentinel);

        Assert.NotEqual(hmacHash, plainHash);
    }

    // ── disabled → enabled mid-stream genesis ────────────────────────────────

    [Fact]
    public async Task TamperEvidence_DisabledThenEnabled_PreGenesisEntriesExcluded()
    {
        // Write 2 entries WITHOUT tamper-evidence (no hasher).
        var auditOff = BuildAuditService(null);
        await auditOff.LogPublishAsync("@a/foo", "1.0.0", "alice", null);
        await auditOff.LogPublishAsync("@a/foo", "1.0.1", "bob", null);

        // Enable tamper-evidence — write 3 more entries.
        var hasher   = BuildHasher();
        var auditOn  = BuildAuditService(hasher);
        await auditOn.LogPublishAsync("@a/foo", "1.0.2", "carol", null);
        await auditOn.LogPublishAsync("@a/foo", "1.0.3", "dave", null);
        await auditOn.LogPublishAsync("@a/foo", "1.0.4", "eve",  null);

        var (valid, firstBrokenId, genesisId, checkedCount) = await VerifyChainAsync(hasher);

        // Verify sees only the 3 hashed entries.
        Assert.True(valid,        "Post-genesis chain of 3 should be valid");
        Assert.Null(firstBrokenId);
        Assert.Equal(3, checkedCount);
        Assert.NotNull(genesisId);

        // Confirm the 2 pre-genesis entries have null hashes.
        var all = await _dbContext.AuditLog.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        Assert.Null(all[0].EntryHash);
        Assert.Null(all[1].EntryHash);
        Assert.NotNull(all[2].EntryHash);
    }

    [Fact]
    public async Task TamperEvidence_DisabledThenEnabled_GenesisIdIsThirdEntry()
    {
        var auditOff = BuildAuditService(null);
        await auditOff.LogPublishAsync("@a/foo", "1.0.0", "alice", null);
        await auditOff.LogPublishAsync("@a/foo", "1.0.1", "bob",   null);

        var hasher  = BuildHasher();
        var auditOn = BuildAuditService(hasher);
        await auditOn.LogPublishAsync("@a/foo", "1.0.2", "carol", null);

        var allEntries   = await _dbContext.AuditLog.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        int thirdEntryId = allEntries[2].Id;

        var (_, _, genesisId, _) = await VerifyChainAsync(hasher);
        Assert.Equal(thirdEntryId, genesisId);
    }

    [Fact]
    public async Task TamperEvidence_DisabledThenEnabled_PreGenesisNotReportedBroken()
    {
        // Even if we mutate a pre-genesis (null-hash) entry, verify should not report it broken.
        var auditOff = BuildAuditService(null);
        await auditOff.LogPublishAsync("@a/foo", "1.0.0", "alice", null);

        var hasher  = BuildHasher();
        var auditOn = BuildAuditService(hasher);
        await auditOn.LogPublishAsync("@a/foo", "1.0.1", "bob",   null);
        await auditOn.LogPublishAsync("@a/foo", "1.0.2", "carol", null);

        // Mutate the pre-genesis entry — verify should not see it.
        var preGenesis = await _dbContext.AuditLog.OrderBy(e => e.Id).FirstAsync();
        preGenesis.User = "tampered";
        await _dbContext.SaveChangesAsync();

        var (valid, firstBrokenId, _, checkedCount) = await VerifyChainAsync(hasher);

        Assert.True(valid,    "Pre-genesis mutation must not cause valid=false");
        Assert.Null(firstBrokenId);
        Assert.Equal(2, checkedCount);
    }

    // ── Retention × tamper ────────────────────────────────────────────────────

    [Fact]
    public async Task TamperEvidence_RetentionDeletesGenesis_VerifyStillValid()
    {
        // Strategy: Build a 4-entry chain where the genesis entry has an old timestamp
        // that will be pruned by retention. To keep timestamps consistent with their
        // canonical payloads (mutating a timestamp after hashing would corrupt the chain),
        // we build the chain by directly writing entries to the DB with the correct hash
        // fields computed upfront using a fixed old timestamp.

        var hasher = BuildHasher();
        var oldTs   = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var recentTs = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);

        // Entry 1 — genesis (will be pruned by retention).
        var e1 = new AuditEntry
        {
            Action    = "package.publish",
            Package   = "@a/foo",
            Version   = "1.0.0",
            User      = "genesis-user",
            Decision  = "allow",
            Timestamp = oldTs,
        };
        string prev1 = AuditChainHasher.GenesisSentinel;
        e1.PreviousHash = prev1;
        e1.EntryHash    = hasher.ComputeEntryHash(e1, prev1);
        await _db.AddAuditEntryAsync(e1);

        // Entries 2–4 — recent (will be retained).
        string? prevHash = e1.EntryHash;
        for (int i = 1; i <= 3; i++)
        {
            var e = new AuditEntry
            {
                Action    = "package.publish",
                Package   = "@a/foo",
                Version   = $"1.0.{i}",
                User      = $"user{i}",
                Decision  = "allow",
                Timestamp = recentTs,
            };
            e.PreviousHash = prevHash!;
            e.EntryHash    = hasher.ComputeEntryHash(e, e.PreviousHash);
            prevHash       = e.EntryHash;
            await _db.AddAuditEntryAsync(e);
        }

        // Confirm chain is valid before deletion.
        var (validBefore, _, _, countBefore) = await VerifyChainAsync(hasher);
        Assert.True(validBefore, "Chain of 4 should be valid before pruning");
        Assert.Equal(4, countBefore);

        // Run retention sweep — delete entries older than 30 days from recentTs.
        var cutoff = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        await _db.DeleteAuditEntriesOlderThanAsync(cutoff);

        // Genesis row is gone; the 3 newer rows remain.
        Assert.Equal(3, await _dbContext.AuditLog.CountAsync());

        // Verify should still report valid=true; it now anchors at the earliest retained hashed row.
        // The anchor's stored previousHash = e1.EntryHash (the deleted genesis hash), which is
        // trusted as the window anchor rather than verified against a prior row.
        var (validAfter, firstBrokenId, genesisId, countAfter) = await VerifyChainAsync(hasher);
        Assert.True(validAfter,  "After genesis deletion, retained chain should still be valid");
        Assert.Null(firstBrokenId);
        Assert.Equal(3, countAfter);
        Assert.NotNull(genesisId);
    }

    [Fact]
    public async Task TamperEvidence_RetentionDeletesAllHashed_VerifyReturnsZero()
    {
        var hasher = BuildHasher();
        var audit  = BuildAuditService(hasher);

        await audit.LogPublishAsync("@a/foo", "1.0.0", "alice", null);

        // Backdate the single entry so retention deletes it.
        var entry = await _dbContext.AuditLog.FirstAsync();
        entry.Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _dbContext.SaveChangesAsync();

        var cutoff = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await _db.DeleteAuditEntriesOlderThanAsync(cutoff);

        var (valid, _, _, checkedCount) = await VerifyChainAsync(hasher);
        Assert.True(valid,      "Empty hashed chain should be trivially valid");
        Assert.Equal(0, checkedCount);
    }

    // ── GetLatestHashedEntryHashAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetLatestHashedEntryHashAsync_NoEntries_ReturnsNull()
    {
        string? result = await _db.GetLatestHashedEntryHashAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestHashedEntryHashAsync_MixedEntries_ReturnsMostRecentHashedHash()
    {
        // Write one unhashed, then one hashed.
        var unhashed = new AuditEntry
        {
            Action    = "auth.login.success",
            User      = "alice",
            Timestamp = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc),
        };
        var hashed = new AuditEntry
        {
            Action       = "package.publish",
            Package      = "@a/foo",
            User         = "bob",
            Timestamp    = new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc),
            PreviousHash = AuditChainHasher.GenesisSentinel,
            EntryHash    = "aaabbbccc",
        };
        var hashed2 = new AuditEntry
        {
            Action       = "package.publish",
            Package      = "@a/foo",
            User         = "carol",
            Timestamp    = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc),
            PreviousHash = "aaabbbccc",
            EntryHash    = "dddeeefff",
        };

        await _db.AddAuditEntryAsync(unhashed);
        await _db.AddAuditEntryAsync(hashed);
        await _db.AddAuditEntryAsync(hashed2);

        string? result = await _db.GetLatestHashedEntryHashAsync();
        Assert.Equal("dddeeefff", result);
    }

    // ── GetAllHashedAuditEntriesAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetAllHashedAuditEntriesAsync_MixedEntries_ReturnsOnlyHashedIdAscending()
    {
        var unhashed = new AuditEntry
        {
            Action    = "x",
            Timestamp = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc),
        };
        var h1 = new AuditEntry
        {
            Action = "a", Timestamp = new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc),
            PreviousHash = "genesis", EntryHash = "hash1",
        };
        var h2 = new AuditEntry
        {
            Action = "b", Timestamp = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc),
            PreviousHash = "hash1",  EntryHash = "hash2",
        };

        await _db.AddAuditEntryAsync(unhashed);
        await _db.AddAuditEntryAsync(h1);
        await _db.AddAuditEntryAsync(h2);

        var result = await _db.GetAllHashedAuditEntriesAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("hash1", result[0].EntryHash);
        Assert.Equal("hash2", result[1].EntryHash);
        // Ordered by id ascending (id is monotonic; result[0].Id < result[1].Id).
        Assert.True(result[0].Id < result[1].Id);
    }
}
