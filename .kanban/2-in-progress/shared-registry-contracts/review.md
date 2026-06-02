## shared-registry-contracts — Review (pass 2)

> Produced by `/feature-review`. Second (final) review pass after pass-1's five findings (1 MEDIUM, 4 LOW) were all marked fixed.

**Scope reviewed:** commits `975ffec8..c1a130aa` on branch `feature/shared-registry-contracts` — with focused regression scrutiny on the five fix commits `3462af44`, `5581e0f0`, `db9cfdad`, `99a634ac`, and the chore commits that follow them.
**Brief:** ./brief.md
**Generated:** 2026-06-03

**Verdict:** Fix commits land cleanly. **One MINOR doc-consistency regression** found: the F04 `UserRoles` move was reflected in three of four doc spots that name the wire-visible set, but the Cross-Cutting Concerns table in the brief (line 235) still enumerates six domains and omits `UserRoles`. No code regressions; the code-level invariants all still hold. Status → `in_progress` (one MINOR finding) per the `resolved`-iff-zero rule.

**Per-fix regression confirmation:**

- **F01 (`3462af44` — `ContractsAssemblyShapeTests`):** Exact-match `HashSet<string>` with `StringComparer.Ordinal` over `{"StashRegistry", "Stash.Core"}` replaces the broken substring check; `IsForbiddenAssemblyName_TeethTest` plants both forbidden names AND a safe name (`System.Text.Json`) and asserts the predicate is exact-match correct (positive AND negative arms). The other two assertions in the class (`ContractsCsproj_ContainsZeroProjectReferences`, `ContractsAssembly_AllPublicTypesInCorrectNamespace`) are byte-identical to pre-fix — no weakening. Test count goes 3 → 4 (+1) as expected.
- **F02 (`5581e0f0` — IL2026 suppression justification):** Both `[UnconditionalSuppressMessage]` attributes are still in place on `DeprecatePackageRequest.Message` and `DeprecateVersionRequest.Message` (necessary for ILC, which does not honor cross-assembly suppressions — proven empirically by the resolver). New justification is anchored on verified evidence: "AOT publish empirically clean … the CLI has zero calls to `Validator.*`, `ValidateObject`, or `ValidateValue`" — matches pass-1's suggested-fix option #1. The added `[RequiresUnreferencedCode]`/`ICollection.Count` color is descriptive, not load-bearing. Shared contract still compiles (no new `using` additions beyond the existing `System.Diagnostics.CodeAnalysis`). Baseline confirms AOT publish stays clean.
- **F04 (`db9cfdad` — `UserRoles` move) — HIGH-RISK FIX, scrutinised hardest:**
  - Single definition confirmed: `grep -rn "class UserRoles"` finds exactly one — `Stash.Registry.Contracts/BoundedDomains.cs:141`. The old definition in `Stash.Registry/Auth/RegistryAuthConstants.cs` is **removed**, not duplicated.
  - Wire values unchanged: `User = "user"`, `Admin = "admin"` — identical pre/post move; the new `BoundedDomainPlacementTests.UserRoles_WireValues_Unchanged` asserts both literals explicitly.
  - Still `const string` (not `static readonly`) — preserves the cross-assembly compile-time inlining invariant EF and field initializers depend on; `BoundedDomainPlacementTests.UserRoles_{User,Admin}_IsConstString` enforces it.
  - All 17 call sites continue to resolve: `AuthController.cs` (8), `AdminController.cs` (3), `ScopesController.cs` (2), `LocalAuthProvider.cs` (1), `RegistryAuthzPrincipalFactory.cs` (1), `RegistryDbContext.cs` (1) for `Stash.Registry`; `NoMagicAuthStringsMetaTests.cs` (good-snippet fixture, 1). The two files that previously got `UserRoles` from the same-namespace `Stash.Registry.Auth` (`LocalAuthProvider.cs`, `RegistryAuthzPrincipalFactory.cs`) had `using Stash.Registry.Contracts;` correctly added; the other registry files already had that using (for the other wire-visible domains).
  - `NoMagicAuthStringsMetaTests` is not weakened: it is a syntactic scan for `UserRoles.Admin` / `RegistryClaims.*` token text on the LHS of auth-sink call expressions (`IsInRole`, `RequireClaim`, …) — assembly-agnostic. The move is invisible to it.
  - `BoundedDomainPlacementTests` now asserts seven const-class homes (was six); class doc-comment updated (`six` → `seven`); four new facts pass (Lives, User const-string, Admin const-string, WireValues). Test count goes 30 → 34 (+4) as expected.
  - **Pass-1 F04's stated follow-on consequence is closed:** the CLI sink-scan (`CliNoMagicWireStringsMetaTests`) treats `Role` as a sink on **any** type in the `Stash.Registry.Contracts` namespace; `CreateUserRequest.Role` is in that namespace, so a future inlined `Role = "admin"` in `Stash.Cli` would now trip the scan and force a `UserRoles.Admin` reference. The loop F04 was opened to close is closed.
- **F03 / F05 (`99a634ac` — doc precision):** Brief acceptance-criterion language is now "The wire surface is preserved (with one intentional exception)" and explicitly enumerates the `AuditEntry.Id` drop; matching Decision Log row is updated identically. `Stash.Registry/CLAUDE.md` directory tree now correctly draws `Stash.Registry.Contracts/` as a sibling block at repo root (closed `Stash.Registry/` first with a proper `└─`, then opened a new top-level tree).

**Headline invariants — all still hold:**
- `Stash.Registry.Contracts/Stash.Registry.Contracts.csproj` has zero `<ProjectReference>` elements (verified by `grep -c`, count is 0; `ContractsCsproj_ContainsZeroProjectReferences` is unchanged).
- Single source of truth — no CLI shadow DTOs (`CliContractsConsumptionTests` unchanged; F04 specifically eliminated the last remaining wire-visible-but-server-internal const class).
- `CliNoMagicWireStringsMetaTests` still deterministic + teethy: `MinScannedFiles = 20` floor guard present, `AssertBindingFloor` probes a real type (`AssignRoleRequest`), TPA-based reference closure, positive + negative self-tests construct real contracts types, empty exemption list.

**Baseline:** `failed=0 passed=12997 skipped=6` — the +5 delta vs pass-1 (12992) matches the F01 teeth self-test (+1) and the four F04 `UserRoles` placement facts (+4). AOT publish re-verified clean during F02 resolution.

---

## F06 — [MINOR] Brief's Cross-Cutting Concerns table omits `UserRoles` from the wire-visible-domain enumeration after F04

**Status:** open
**Files:** `.kanban/2-in-progress/shared-registry-contracts/brief.md:235`
**Phase:** cross-phase (doc-precision follow-up to F04 + F03/F05)
**Commit:** `db9cfdad` (F04 fix landed `UserRoles` in the shared project but did not propagate the rename to this row); `99a634ac` (F03/F05 fix updated three of four doc spots but did not touch this one)

### Observation

The brief's Cross-Cutting Concerns table at line 235 still enumerates the wire-visible bounded-domain set as **six** entries, omitting `UserRoles`:

> "**Bounded-domain single source of truth** for wire-visible closed sets (package roles, token scopes, visibility, principal types, scope-owner types, **org roles**) referenced by both the registry and the CLI | The `const string` sets in `Stash.Registry.Contracts` (moved out of `Stash.Registry/Auth/RegistryAuthConstants.cs` in P2) | …"

After F04, the wire-visible set is **seven** entries. Three other doc spots that name the same set were updated to seven and now disagree with this row:

- `brief.md:135` (Design / End state) — lists all seven, including `UserRoles`.
- `brief.md:334` (Decision Log "Wire-visible / server-internal split" row) — lists all seven (updated as part of F04's brief edits).
- `Stash.Registry/CLAUDE.md:63` (directory tree) — lists all seven (updated by F05).
- `Stash.Registry/CLAUDE.md:183` (wire-visible vs server-internal bullet) — lists all seven (updated by F05).

The Cross-Cutting Concerns row is the odd one out.

### Why this matters

This is precisely the same doc-consistency class as pass-1's F03 ("wire shape is unchanged" claim contradicted by the `AuditEntry.Id` drop) and F05 (CLAUDE.md tree mis-nesting). The Cross-Cutting Concerns table is also the most architecturally weighty row in the brief — it is the row a future contributor reads to understand which closed sets must live in the shared project and which omission-prevention guards cover them. Leaving it at six creates the same kind of "the docs say six but the code has seven" friction that F04's review-finding called out (in the mirror direction): a future implementer adding an 8th wire-visible domain would consult this row and miss `UserRoles` as precedent.

Severity is MINOR (not MEDIUM) because: (a) the runtime invariant is intact — `UserRoles` is in the shared project, all the meta-tests cover it, the test-suite is green; (b) the other three doc spots are consistent and explicitly enumerate seven; (c) operational impact is zero (registry is pre-release, no external readers of this brief). The fix is a one-word edit ("org roles" → "org roles, user roles") and is in scope for the same doc-precision pass that F03/F05 began.

### Suggested fix

Edit `brief.md:235`. Change:

> "(package roles, token scopes, visibility, principal types, scope-owner types, org roles)"

to:

> "(package roles, token scopes, visibility, principal types, scope-owner types, org roles, user roles)"

No other edits required (the rest of the row — guard mechanism, sink set, rejection rationale — already covers all seven correctly).

### Verify

No code change required. After the brief edit, run a sanity grep:

```
grep -E "(package roles|principal types).*org roles" .kanban/2-in-progress/shared-registry-contracts/brief.md /home/heisen/stash-shared-registry-contracts/Stash.Registry/CLAUDE.md
```

Every match should now end with "user roles" (or the equivalent listing the seventh domain).
