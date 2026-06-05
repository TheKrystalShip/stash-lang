# registry-audit-log-v2 — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve registry-audit-log-v2 Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `009fcf30..40ace200` on branch `feature/registry-audit-log-v2`
**Brief:** ../brief.md
**Generated:** 2026-06-05

**Confidence on tamper-evidence correctness:** **High** for the load-bearing properties — single `AuditChainHasher.CanonicalPayload` reused by both write and verify (proven by `VerifyChainAsync` delegating to `AuditChainHasher.WalkChain` — the exact code path the controller uses); the serialized-append `WriteLock` is `static` so it survives `AuditService` being `Scoped`; only `AuditService.AddEntryAsync` calls `_db.AddAuditEntryAsync` (single chokepoint, verified by grep); EF/SQLite timestamp round-trip handled by `SpecifyKind(Utc)` + fixed 7-digit ISO format and exercised end-to-end by `TamperEvidence_FiveEntries_VerifiesValid` writing through the real `AddEntryAsync` and verifying via the real walker; `GetLatestHashedEntryHashAsync` uses MAX(id) over rows with non-null `entry_hash` (correct insertion-order tail); retention × tamper anchor semantics match the brief and are exercised by `TamperEvidence_RetentionDeletesGenesis_VerifyStillValid`. The OpenAPI snapshot diff is purely additive (+203 / -0). **One spec/behavior divergence: F02** — the brief's "new genesis at each enabled→disabled→enabled re-enable" rule is unimplemented (the write path silently bridges the gap) and untested. **One resource-safety concern: F01** — the verify endpoint materializes the entire hashed chain into memory.

**Findings by severity:** 0 CRITICAL, 0 HIGH, 2 MEDIUM, 2 LOW.

---

## F01 — [MEDIUM] verify endpoint materializes the entire hashed chain into memory

**Status:** open
**Files:** `Stash.Registry/Database/StashRegistryDatabase.cs:916-923`, `Stash.Registry/Controllers/AdminController.cs:435`, `Stash.Registry/Services/AuditChainHasher.cs:205-239`
**Phase:** A6
**Commit:** bb7094cd, 11cfbe5c

### Observation

`GET /api/v1/admin/audit-log/verify` calls `_db.GetAllHashedAuditEntriesAsync()`, which is implemented as:

```csharp
return await _context.AuditLog
    .Where(e => e.EntryHash != null)
    .OrderBy(e => e.Id)
    .AsNoTracking()
    .ToListAsync();
```

The full hashed chain is buffered into a `List<AuditEntry>` before `AuditChainHasher.WalkChain` walks it. The controller and the unit-test helper (`VerifyChainAsync`) both flow through this same shape, so the unit tests don't surface the issue.

This is the **only** non-streamed surface introduced by the feature. The list endpoint pages (200 per page), the export endpoint streams via `AuditService.StreamAuditLogAsync` (page-200 internal loop, `IAsyncEnumerable`), but verify does not.

### Why this matters

The audit table is *designed* to grow large under this feature:

- `Audit.RetentionDays = 0` is the default and explicitly means "never delete" (`AuditConfig.cs:17-21` + brief Decision Log row 13: "compliance log whose retention obligation is unknown at install time").
- `auth.login.failure` is in scope and the brief flags it as high-volume ("Failed logins are higher-volume than mutations … could grow the table and lengthen the tamper chain. It is **kept in**.").

So on a long-lived registry with tamper-evidence enabled, the hashed-entry count grows without bound. An admin hitting `/admin/audit-log/verify` allocates one in-memory `AuditEntry` per hashed row — at multi-million rows that's an admin-triggered OOM. The verify endpoint also blocks a Kestrel request thread for the entire walk (no `await` yield between `ComputeEntryHash` calls).

This is not a correctness break (small chains verify correctly) and not a security hole, but it is a resource-safety regression an operator will hit at the worst moment — when they actually need to verify integrity after long uptime.

### Suggested fix

Walk the chain incrementally. Two equivalent options:

1. **Page-based walk in the controller / service** — mirror `StreamAuditLogAsync`'s `pageSize=200` pattern: add `GetHashedAuditEntriesPageAsync(int afterId, int pageSize)` (or expose `IAsyncEnumerable<AuditEntry>`), have `WalkChain` accept an `IAsyncEnumerable<AuditEntry>` (or expose a small async-fold helper), and keep only `prior.EntryHash` + the running `(firstBrokenId, genesisId, checkedCount)` between iterations.
2. **EF streaming** — change `GetAllHashedAuditEntriesAsync` to expose `IAsyncEnumerable<AuditEntry>` via `AsAsyncEnumerable()` (no `ToListAsync`) and adapt `WalkChain` to an async fold. SQLite/EF both support this.

Either way the verify walker only needs the prior entry's `EntryHash` and the running counters, so memory drops to O(1) and the per-iteration `await` yields to other requests.

### Verify

```
dotnet test --filter "FullyQualifiedName~AuditTamperEvidence"
```

Add a stress test that seeds ≥10k hashed entries and asserts verify completes without buffering, then re-run.

---

## F02 — [MEDIUM] enabled→disabled→enabled gap is silently bridged; brief's "new genesis at re-enable" rule is unimplemented and untested

**Status:** open
**Files:** `Stash.Registry/Services/AuditService.cs:75-92`, `Stash.Registry/Services/AuditChainHasher.cs:205-239`, `Stash.Tests/Registry/AuditTamperEvidenceTests.cs` (no covering test)
**Phase:** A6
**Commit:** bb7094cd

### Observation

The brief (Design → Tamper-evidence, lines 336-341) documents:

> "enabled → disabled → re-enabled: the simplest rule is a **new genesis** at each re-enable (a `null`-hash gap delimits chains); `verify` reports each contiguous hashed run and treats a pre-genesis gap as a boundary, not a break."

The implementation does not implement that rule. On re-enable, `AuditService.AddEntryAsync` calls `_db.GetLatestHashedEntryHashAsync()`, which returns the `EntryHash` of the row with MAX(id) among rows with non-null `entry_hash`. With a sequence `[E1 hashed, E2 hashed, E3 hashed, E4 unhashed, E5 unhashed, E6 hashed (re-enable)]`, `GetLatestHashedEntryHashAsync()` returns `E3.EntryHash` — so `E6.PreviousHash = E3.EntryHash`, **not** `AuditChainHasher.GenesisSentinel`.

Consequently `WalkChain` (which already filters out unhashed rows via `GetAllHashedAuditEntriesAsync`) sees a single continuous chain `[E1, E2, E3, E6]` and reports `valid=true` with `genesisId = E1.Id` — one chain, original genesis. The gap is invisible.

The test file covers only single-transition cases:
- `TamperEvidence_DisabledThenEnabled_PreGenesisEntriesExcluded`
- `TamperEvidence_DisabledThenEnabled_GenesisIdIsThirdEntry`
- `TamperEvidence_DisabledThenEnabled_PreGenesisNotReportedBroken`

There is **no `EnabledThenDisabledThenEnabled` test**. The double-transition behavior is therefore both undocumented (the brief documents the opposite) and unverified.

Note this is **not** a correctness break: small chains still verify correctly. The chosen implementation is also defensible — if the implementation followed the brief literally and stamped `E6.PreviousHash = GenesisSentinel`, the single `WalkChain` linkage check (`entry.PreviousHash == hashedEntries[i-1].EntryHash`) would false-positive-break at every re-enable point.

### Why this matters

A brief decision documents the contract the implementation is supposed to satisfy. When the implementation chooses different semantics — for good engineering reasons — that divergence has to land *somewhere*: the brief, the tests, or both. Currently it lands nowhere, which is the worst outcome:

- A future maintainer reading the brief and looking for the re-enable boundary in `verify`'s response (e.g. for an audit-period boundary report) will be surprised that re-enables are invisible.
- A future change that tries to honor the brief literally (stamp `GenesisSentinel` at re-enable) would silently break the walker — there is no test to catch the false-positive.
- An operator who tampered with a pre-gap entry and re-enabled tamper-evidence may believe pre-gap tampering is detected, because the brief says each enable creates a new chain — but the current walker still anchors at the original genesis.

### Suggested fix

Pick one of two consistent end-states in the resolver turn:

(a) **Keep the bridged-chain behavior; correct the brief and add a test.** Update brief Decision Log + the tamper-evidence section to say "re-enable links to the last hashed entry; null-hash gaps are invisible to the walker." Add a `TamperEvidence_EnabledDisabledEnabled_BridgesChainAcrossGap` test that performs the off→on→off→on sequence and asserts `valid=true`, `genesisId = E1.Id` (the original).

(b) **Implement the brief literally.** Stamp `GenesisSentinel` on the first entry after each re-enable (detect the gap by seeing whether any non-hashed entry exists with `id > GetLatestHashedEntry.Id`, or persist an explicit "tamper-evidence epoch" column). Extend `WalkChain` to recognize segment boundaries (when `entry.PreviousHash == GenesisSentinel` at `i > 0`, reset the linkage anchor). Update `AuditVerifyResponse` to optionally report each segment's `genesisId`. Add the re-enable test.

The reviewer's recommendation is **(a)** — the bridged behavior is mechanically simpler and gives stronger continuity guarantees — but the resolver MUST NOT silently pick (a) without flipping the brief language too.

### Verify

```
dotnet test --filter "FullyQualifiedName~AuditTamperEvidence"
```

The new `TamperEvidence_EnabledDisabledEnabled_*` test must be green; if (a) is chosen, the brief diff must accompany the code commit so the discrepancy doesn't recur.

---

## F03 — [LOW] AuditEntry.Action XML doc examples reference non-existent wire values

**Status:** open
**Files:** `Stash.Registry/Database/Models/AuditEntry.cs:19-20`
**Phase:** A1
**Commit:** 50b5f894 (predates A1 — example values were not refreshed during the consolidation sweep)

### Observation

The XML doc summary on `AuditEntry.Action` reads:

```csharp
/// <summary>The action type string, e.g. <c>"publish"</c>, <c>"unpublish"</c>, <c>"user_create"</c>, <c>"token_revoke"</c>.</summary>
public string Action { get; set; } = "";
```

The example wire values `"user_create"` and `"token_revoke"` (underscore form) do not match the actual wire values in `AuditActions.cs`, which are `"user.create"` (`AuditActions.UserCreate`) and `"token.revoke"` (`AuditActions.TokenRevoke`) — dot form. This XML doc was not updated alongside A1's byte-for-byte migration.

The brief is very explicit (Decision Log row 2, Migration table, Notes column on A1) that the underscore form is reserved only for `package.visibility_change` and `token_theft_detected`, and that "tidy" reformatting to dotted/underscored forms must NOT happen — the XML doc here gives exactly the wrong impression.

### Why this matters

This is pure documentation drift, no behavioral impact. The cost is that a future contributor reading this XML doc and inferring the wire shape from these examples will introduce exactly the value-change regression that, per brief Decision Log row 2 and the project memory `feedback-no-magic-strings`, broke download-metrics. The fix is a one-line edit.

### Suggested fix

Update the XML doc to reference the actual canonical wire values, e.g.:

```csharp
/// <summary>The action type string from <see cref="Services.AuditActions"/>, e.g. <c>"package.publish"</c>, <c>"user.create"</c>, <c>"token.revoke"</c>, <c>"auth.login.success"</c>.</summary>
```

Reference `AuditActions` directly so this can't drift again.

### Verify

```
dotnet build Stash.Registry
```

(Pure XML-doc; no behavioral test needed. The `NoMagicAuditActionStringsMetaTests` chokepoint also continues to pass — XML-doc strings are not audit-action sinks.)

---

## F04 — [LOW] HashSecret silently falls back from base64 to UTF-8 bytes on parse failure

**Status:** open
**Files:** `Stash.Registry/Services/AuditChainHasher.cs:153-162`
**Phase:** A6
**Commit:** bb7094cd

### Observation

```csharp
if (!string.IsNullOrEmpty(_config.HashSecret))
{
    // HMAC-SHA256 keyed with the operator-supplied base64 secret.
    byte[] key;
    try   { key = Convert.FromBase64String(_config.HashSecret); }
    catch { key = Encoding.UTF8.GetBytes(_config.HashSecret); }

    using var hmac = new HMACSHA256(key);
    return Convert.ToHexString(hmac.ComputeHash(input)).ToLowerInvariant();
}
```

The brief (Configuration section, lines 161-168) advertises the secret as `"HashSecret": "<base64>"` — a base64-encoded key. The comment on line 155 also calls it "the operator-supplied base64 secret." But on a parse failure the code silently falls back to using the raw string's UTF-8 bytes as the key. So `"correcthorsebatterystaple"` (not valid base64) is accepted as a 25-byte HMAC key, with no log, no warning, no startup-time validation.

This is not a security hole — both branches produce a deterministic key of adequate length for HMAC-SHA256, and an attacker who can mutate the DB has no key access either way. But it muddies the operational contract in a way that turns a future typo-fix into "my audit log is corrupted":

1. An operator who *intended* base64 but typo'd padding gets a different HMAC key than they think they did. If they fix the typo later, the entire historical chain becomes unverifiable (the effective key changed), and verify will report `valid=false` with no diagnosable cause.
2. An operator reading the code may use UTF-8 input; one reading the brief uses base64. Identical wire surface, different cryptographic semantics — and neither is logged on startup.

### Why this matters

The fallback turns a silent operator-input error into a future-tense audit-log-corruption symptom. Failing fast at startup (or requiring an explicit encoding prefix) makes the contract enforceable and the failure visible at the moment it can be fixed, not weeks/months later when the chain breaks.

### Suggested fix

Choose one of:

(a) **Strict base64** — drop the `catch` fallback; throw at hasher construction with a clear "HashSecret is not valid base64" message, OR validate at startup in `Startup.cs` when `Audit.TamperEvidence.Enabled` is `true`.

(b) **Explicit prefix** — accept `base64:...` or `utf8:...` prefixes; reject unprefixed strings. Update the brief's example to show the prefix.

The cheap fix is (a). Either way: also log (without the key itself) which path was taken on startup so an operator can confirm they're in the mode they think.

### Verify

```
dotnet test --filter "FullyQualifiedName~AuditTamperEvidence"
```

Add a `TamperEvidence_InvalidBase64HashSecret_FailsClosedAtStartup` test that points at `"not valid base64!"` and asserts the configured behavior (throw, or use the prefix path).
