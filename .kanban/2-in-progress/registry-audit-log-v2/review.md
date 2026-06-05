# registry-audit-log-v2 — Review

> Produced by `/feature-review` (pass 2). One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve registry-audit-log-v2 Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.
>
> Pass-1 findings (F01–F04) were all marked fixed in `3f3589dd` (F01/F02) and `22f0ae83` (F03/F04). This pass-2 re-review is the single re-review allowed by the autopilot loop; it audits the fixes for regressions and looks for anything pass 1 missed.

**Scope reviewed:** commits `009fcf30..20d6406d` on branch `feature/registry-audit-log-v2`
**Brief:** ../brief.md
**Generated:** 2026-06-06

---

**Verdict on F01 streaming rewrite (verify-semantics preservation):**
**Semantics preserved.** The old `WalkChain(IReadOnlyList)` and the new `WalkChainAsync(IAsyncEnumerable)` produce the identical `(valid, firstBrokenId, genesisId, checkedCount)` tuple for every covered case (intact chain, tampered content, tampered linkage, empty input, disabled→enabled, retention-truncated, enabled→disabled→enabled bridged). Verified by:

1. **`CheckedCount` semantics — same.** Old code returned `hashedEntries.Count` (always the full materialised list size — `break`-ing past a broken entry did NOT reduce the reported count). New code increments `checkedCount++` per iteration and never short-circuits, so it also returns the total. Both = "total hashed entries fetched."
2. **`FirstBrokenId` semantics — same.** Old code `break`s on the first failure; new code uses `if (firstBrokenId == null) firstBrokenId = entry.Id` to record only the first. The check-order swap (new code does linkage check before content check for non-anchor entries, old code did content first) does **not** change the recorded id when both checks fail for the same entry (same id either way) and does not change which id is recorded across entries.
3. **Anchor handling — same.** Both treat `hashedEntries[0]` / first-yielded entry as the anchor: trust the stored `PreviousHash`, perform only the content check. New code's `genesisId == null` guard fires exactly once at the first yield. Confirmed equivalent.
4. **`GenesisId` — same.** Both return the first hashed entry's id (the walker's anchor). `TamperEvidence_EnabledDisabledEnabled_BridgesChainAcrossGap` proves the bridged case: 3 hashed + 2 null + 1 hashed yields `genesisId = E1.Id`, `checkedCount = 4`.
5. **No buffering on the new path.** `StreamHashedAuditEntriesAsync` returns `_context.AuditLog...AsAsyncEnumerable()` — no `.ToList`/`.ToListAsync`. `WalkChainAsync` holds only `(priorEntryHash, genesisId, firstBrokenId, checkedCount)` between yields. O(1) memory confirmed.
6. **Single walker invariant preserved.** Controller (`AdminController:435-440`) and test helper (`AuditTamperEvidenceTests.VerifyChainAsync`) both call the SAME `WalkChainAsync` over `StreamHashedAuditEntriesAsync()`. No second walk path reintroduced; greps for `GetAllHashedAuditEntriesAsync` and the old `WalkChain(IReadOnlyList<>)` overload return zero hits in production code.
7. **Inefficiency, not a correctness break.** The new code continues iterating after a break is found (it just stops recording new firstBrokenId values). The original `break` was a performance optimization that the rewrite drops — wasteful CPU on a known-broken chain but does NOT change the observable result tuple. Not raised as a finding because the asymptotic memory win (O(n) → O(1)) is orders of magnitude more valuable than the lost early-exit on a broken chain.

**F02 brief/code agreement.** The brief diff at lines 336-342 now describes exactly what `AuditService.AddEntryAsync` + `GetLatestHashedEntryHashAsync` + `WalkChainAsync` actually do (link re-enable to the most recent hashed entry, null-hash gaps invisible, one continuous chain anchored at the original genesis). The new `EnabledDisabledEnabled_BridgesChainAcrossGap` test asserts `valid=true`, `genesisId=E1.Id`, `checkedCount=4` — matches the brief. No lingering contradiction.

**F03 doc fix.** `AuditEntry.Action`'s XML summary now lists real wire values (`package.publish`, `user.create`, `token.revoke`, `auth.login.success`) and points at `Services.AuditActions` via `<see cref>`. Verified.

**F04 fail-closed at startup.** `AuditChainHasher`'s constructor (lines 79-93) calls `Convert.FromBase64String` strictly and throws `InvalidOperationException` on `FormatException` when `Enabled=true` and `HashSecret` is non-empty. `Startup.cs:222` registers `services.AddSingleton(new AuditChainHasher(...))` with an **eagerly-constructed instance** — the validation runs at `ConfigureServices` time, so a misconfigured operator hits the throw at startup, not at the first audit write. The previous `try/catch { key = Encoding.UTF8.GetBytes(...) }` fallback in `ComputeEntryHash` (lines 153-162 of the pre-fix file) is fully removed; the new `ComputeEntryHash` decode is bare `Convert.FromBase64String(_config.HashSecret)` (now known-safe by construction). Three new tests cover invalid base64 throws, valid base64 constructs, disabled-with-bad-secret no-ops. Verified.

**Findings by severity:** 0 CRITICAL, 0 IMPORTANT, 2 MINOR.

The pass-1 findings (F01–F04) are all sound. Pass-2 surfaces two cosmetic doc-drift items introduced by the fix commits themselves; neither blocks `/done`.

---

## F05 — [MINOR] F02 test's XML-doc summary is orphaned by the F04 test insertion

**Status:** open
**Files:** `Stash.Tests/Registry/AuditTamperEvidenceTests.cs:680-723`
**Phase:** A6 (review-pass-1 fix follow-up)
**Commit:** 22f0ae83

### Observation

When `22f0ae83` (F04) added three new tests, it inserted them between the F02 section's `<summary>` block and the method the summary documents. The file now reads (lines 680-726):

```csharp
    // ── enabled → disabled → enabled (F02) ───────────────────────────────────

    /// <summary>
    /// Validates the implemented "bridged chain" contract: when tamper-evidence is
    /// enabled, then disabled (writing null-hash entries), then re-enabled, the re-enabled
    /// entries link to the most recent hashed entry rather than starting a new genesis.
    /// The walker verifies one continuous chain anchored at the original genesis and ignores
    /// the null-hash gap written during the disabled period.
    /// </summary>
    // ── F04: strict base64 validation at construction ─────────────────────────

    [Fact]
    public void TamperEvidence_InvalidBase64HashSecret_FailsClosed()
    {
        ...
    }
    [Fact]
    public void TamperEvidence_ValidBase64HashSecret_DoesNotThrow() { ... }
    [Fact]
    public void TamperEvidence_InvalidBase64_WhenDisabled_DoesNotThrow() { ... }

    [Fact]
    public async Task TamperEvidence_EnabledDisabledEnabled_BridgesChainAcrossGap()
    {
        ...
    }
```

By C# XML-doc binding, the `<summary>` block now documents `TamperEvidence_InvalidBase64HashSecret_FailsClosed` (the next method declaration), not the F02 test. The F02 banner (`// ── enabled → disabled → enabled (F02) ───`) and the matching summary are stranded above an unrelated test, and the F02 method `TamperEvidence_EnabledDisabledEnabled_BridgesChainAcrossGap` has no XML doc.

### Why this matters

Pure documentation drift, zero behavioural impact. The test runs fine and proves the F02 fix. But a reader scanning the file for the F02 fix lands on the bridged-chain summary attached to an F04 base64 test — misleading. The author's intent was clearly to keep the F02 summary on the F02 method; the F04 insertion silently rebinding it is an editing accident, not a design choice.

### Suggested fix

Move the F04 base64 block (the `// ── F04: ...` header and the three `TamperEvidence_*Base64*` tests) **above** the F02 summary block, or move the F02 summary block directly above its method. Either reorders the file to:

```csharp
    // ── F04: strict base64 validation at construction ─────────────────────────

    [Fact] TamperEvidence_InvalidBase64HashSecret_FailsClosed() { ... }
    [Fact] TamperEvidence_ValidBase64HashSecret_DoesNotThrow() { ... }
    [Fact] TamperEvidence_InvalidBase64_WhenDisabled_DoesNotThrow() { ... }

    // ── enabled → disabled → enabled (F02) ───────────────────────────────────

    /// <summary>
    /// Validates the implemented "bridged chain" contract: ...
    /// </summary>
    [Fact]
    public async Task TamperEvidence_EnabledDisabledEnabled_BridgesChainAcrossGap() { ... }
```

### Verify

```
dotnet build Stash.Tests
dotnet test --filter "FullyQualifiedName~AuditTamperEvidence"
```

(Pure reordering; no behavior change. All affected tests stay green.)

---

## F06 — [MINOR] Two `<see cref="WalkChain">` doc references survived the rename to `WalkChainAsync`

**Status:** open
**Files:** `Stash.Registry/Services/AuditChainHasher.cs:195`, `Stash.Tests/Registry/AuditLogControllerTests.cs:540`
**Phase:** A6 (review-pass-1 fix follow-up)
**Commit:** 3f3589dd

### Observation

The F01 fix renamed `WalkChain(IReadOnlyList<AuditEntry>)` → `WalkChainAsync(IAsyncEnumerable<AuditEntry>)`. Two XML-doc `<see cref="...">` references still point at the old (now non-existent) symbol:

1. `Stash.Registry/Services/AuditChainHasher.cs:193-196` — on `ChainWalkResult`:
   ```csharp
   /// <summary>
   /// The result of walking the tamper-evidence chain, as returned by
   /// <see cref="WalkChain"/>.
   /// </summary>
   public sealed record ChainWalkResult(bool Valid, int? FirstBrokenId, int? GenesisId, int CheckedCount);
   ```
2. `Stash.Tests/Registry/AuditLogControllerTests.cs:540`:
   ```csharp
   /// This is the end-to-end HTTP-level proof that the controller calls <see cref="AuditChainHasher.WalkChain"/>
   /// (the same walker exercised by the unit tests in <c>AuditTamperEvidenceTests</c>).
   ```

Both `<see cref>` targets fail to resolve. Roslyn surfaces them as `CS1574` ("XML comment has cref attribute that could not be resolved") and writes a `<see cref="!:AuditChainHasher.WalkChain"/>` marker into the generated `Stash.Tests.xml` (visible in the `obj/bin` artefacts: `Stash.Tests/obj/Debug/net10.0/Stash.Tests.xml:4493`).

### Why this matters

Pure doc-drift, no behavior impact. A future reader clicking-through in an IDE gets a dead link, and the generated docs ship a broken cross-reference. `CS1574` is suppressed by default but the project's `<TreatWarningsAsErrors>` is currently false for these projects — if a project sweep raises that to true later, both lines will start failing CI as compile errors and require this same fix on a regression hunt.

### Suggested fix

Replace both occurrences:

```csharp
// AuditChainHasher.cs:195
/// <see cref="WalkChainAsync"/>.

// AuditLogControllerTests.cs:540
/// This is the end-to-end HTTP-level proof that the controller calls <see cref="AuditChainHasher.WalkChainAsync"/>
```

### Verify

```
dotnet build Stash.Registry Stash.Tests
```

Confirm no `CS1574` warnings on the touched lines and no surviving `WalkChain` token in the registry/test sources:

```
grep -rn '"WalkChain[^A]' Stash.Registry/ Stash.Tests/Registry/
```

should return zero hits.
