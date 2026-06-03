# Registry Declarative Request Validation — Review (Pass 2)

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header is parseable: `## Fxx — [SEVERITY] short title`.
>
> **Status lifecycle** — `open` blocks `/done`; `fixed` carries `**Fixed in:** <sha>`;
> `accepted` is human-only and requires `**Accepted because:** <reason>`. CRITICAL
> findings can NEVER be `accepted`.

**Scope reviewed:** commits `5ff66c16..1f4efbf4` on branch `feature/registry-declarative-request-validation` (full feature including all pass-1 resolves)
**Brief:** ./brief.md
**Generated:** 2026-06-04 (re-review pass 2)

---

## Summary

Pass 2 of the review. All nine pass-1 findings (F01–F09 in the previous review)
resolved cleanly. The resolutions implement the user's recorded decisions in
`deviations.md` — F01/F02 accepted strict-A direction (documentation-only), F03
restored AuditLogQuery pageSize default to 50, F04 lowercased the `[FromQuery]`
DTO property names so the OpenAPI contract matches the documented
`?q=&page=&pageSize=` wire shape, F05 landed a non-vacuous cross-check meta-test
guarding grammar/parse drift, F06/F07 closed the test coverage gaps, F08 deleted
the dead inline `IsValidScopeName` guard, and F09 named the admin username
pattern and pinned the dual-grammar behaviour with three new tests.

Core invariants verified against pass-1 commitments:

- **`RequestModelBindingMetaTests.KnownExemptions` is empty** and asserted-empty
  (`Stash.Tests/Registry/Validation/RequestModelBindingMetaTests.cs:100-104`).
  No controller regressed to manual `Request.Body`.
- **`AuthzDispatchCoverageMetaTests` imperative-pin is unchanged** —
  `PinnedImperativeActions == {ScopesController.ClaimScope}`
  (`Stash.Tests/Registry/Authz/AuthzDispatchCoverageMetaTests.cs:65-68`). The
  F08 deletion removed only dead code; the `[ImperativeAuthz]` marker, inline
  PDP call, 409 `ScopeReserved`/`ScopeNotOwned` mapping, Trim/lowercase
  normalisation, caller-matches-owner check, `isOrgOwner` check, namespace-pool
  collision checks all remain
  (`Stash.Registry/Controllers/ScopesController.cs:81-148`).
- **`SearchQuery.pageSize` default is still 20**
  (`Stash.Registry.Contracts/SearchContracts.cs:37`); **`AuditLogQuery.pageSize`
  default is restored to 50** matching docs
  (`Stash.Registry.Contracts/AdminContracts.cs:82`).
- **F04 snapshot diff is exactly the 7 param-name flips** (`Q→q`, `Page→page`,
  `PageSize→pageSize` for Search; `Page→page`, `PageSize→pageSize`,
  `Package→package`, `Action→action` for Admin GetAuditLog); no other surface
  drift. ASP.NET `[FromQuery]` binding remains case-insensitive so old-cased
  clients still function.
- **F05 cross-checks are not vacuous.** `GrammarParseAgreementMetaTests`
  directly references `PackageManifest.IsValidScopeName` and
  `AuthHelper.ParseTokenExpiry` (compiled symbol references, not Roslyn scan)
  so a binding-floor probe isn't required. The corpus discriminates: changing
  the attribute regex flips a scope-grammar assertion; removing the ≥1h floor
  flips bucket 2 (`59m`/`30m`/`0d`). The documented divergences (empty-string,
  floor) are pinned explicitly.
- **No-magic-strings doctrine intact.** Bounded domains stay enums;
  `AdminUsernamePattern` is a named `internal const string` on the DTO class
  (regex patterns are doctrine-exempt — CLAUDE.md "Not bounded — exempt"); no
  inline bounded literals introduced.
- **400 wire shape (`ErrorResponse{InvalidRequest, aggregated}`) unchanged.**
  Resolve commits touched no behavior on the factory path.
- **No new behavior change** crept in beyond the three the user authorized
  (F01 strict, F02 team-grammar, F03 default-50 restore + the pre-existing
  `pageSize` reject).

Baseline (per orchestrator input): 13 246 passed, 0 failed, 6 skipped
(pre-existing quarantines). No regression observed in the resolve commits.

One single MINOR finding below — a stale code comment introduced by the F04
resolve. It is non-blocking and explicitly human-acceptable (`/accept`).

| Severity   | Count |
| ---------- | ----- |
| CRITICAL   | 0     |
| IMPORTANT  | 0     |
| MINOR      | 1     |

---

## F01 — [MINOR] Stale property-name reference in SearchController comment after the F04 lowercase rename

**Status:** open
**Files:** `Stash.Registry/Controllers/SearchController.cs:66`
**Phase:** P3 (introduced); F04 (missed during rename)
**Commit:** f7c9ea9b

### Observation

The F04 resolve commit (`f7c9ea9b`) renamed `SearchQuery.Page` → `page` and
`SearchQuery.PageSize` → `pageSize` across the codebase. Member-access sites
in `SearchController.cs` and `AdminController.cs` were updated correctly
(see e.g. `SearchController.cs:73` `query.q`, `query.page`, `query.pageSize`).
A single C# comment at `SearchController.cs:66` was not updated:

```csharp
// [Range] on SearchQuery.Page and SearchQuery.PageSize ensures valid values.
// Out-of-range values return 400 InvalidRequest (replaces the previous silent clamp).
```

The DTO properties referenced by name are now `SearchQuery.page` and
`SearchQuery.pageSize`. The comment still says `Page` / `PageSize`.

### Why this matters

Purely cosmetic — no behavior, build, or test impact. The comment references
property names that no longer exist verbatim. A future maintainer who greps
for `SearchQuery.Page` from a IDE rename-refactor will not find it, and may
briefly trip on the stale identifier before realising it is comment-only.

This is the only resolve-introduced artifact pass-2 surfaced; everything else
on the F04 path (controller member access, OpenAPI snapshot, CLAUDE.md
documentation reference, test object-initializers) was updated. It is the
shape pass-2 exists to catch: a single missed touchpoint in an otherwise
clean rename sweep.

### Suggested fix

One-line edit:

```csharp
// [Range] on SearchQuery.page and SearchQuery.pageSize ensures valid values.
```

Or, equivalently, drop the property-name references entirely and reword as
"The [Range] attributes on SearchQuery ensure valid pagination values."

### Verify

```
dotnet build
# No test added — comment-only edit.
```

This finding is explicitly safe to `/accept` if a human prefers to defer the
cosmetic edit. It is not blocking; functionally everything is correct.

---
