# Registry API Readiness — Phase 1 (web-client prerequisites) — Review

> Produced by `/feature-review`. One finding per H2 section.

**Scope reviewed:** commits `d251d128..45dacee1` on branch `main` (worktree `stash-registry-api-readiness-phase1`)
**Brief:** ../brief.md
**Generated:** 2026-06-04

---

## Summary

The feature ships its declared Bucket-A surface cleanly and the load-bearing safety
properties hold up under scrutiny:

- **Visibility chokepoint is genuinely reused.** `/versions` and `/readme` both
  carry `[PublicEndpoint] + [RegistryAuthorize(RegistryAction.ReadPackageMetadata)]`,
  which the `RegistryAuthorizeFilter` dispatches into `RegistryAuthorizer` →
  `AuthorizePackageReadAsync` — the exact same predicate `GetPackage` uses
  (`RegistryAuthorizer.cs:160-163`). Neither endpoint re-implements visibility,
  neither threads `callerUsername` into a bespoke DB call, and there is no
  separate "exists but hidden vs not found" code path. The three load-bearing rows
  (anon → 404, authed-no-role → 404, reader+ → 200) are exercised through the full
  `WebApplicationFactory` HTTP pipeline in `VersionsEndpointTests` and
  `ReadmeEndpointTests`, not via unit shortcuts.
- **P2 Detect guard has teeth.** `PackagesControllerRegistryAuthorizeRequiredTests`
  binds via `typeof(PackagesController)` (a compile-time hard reference, not an
  `AppDomain.GetAssemblies()` scan, so the CLAUDE.md load-order-determinism
  concern does not apply). It pairs the production-compliance sweep with a
  `MinActionCount >= 13` floor and a `PublicEndpointOnlyFixtureController`
  fail-path fixture; a stray `[PublicEndpoint]`-only action would flip Assertion 1
  red. The dual-attribute fixture also proves the scanner accepts the correct shape.
- **`maxPageSize` drift-impossibility is real.** `PagingLimits.MaxPageSize = 100`
  (`Stash.Registry.Contracts/PagedResponse.cs:28`) is the single `const int` used by
  `SearchQuery.[Range(1, PagingLimits.MaxPageSize)]` (line 100), the new
  `VersionsQuery.[Range(1, PagingLimits.MaxPageSize)]`
  (`PackageContracts.cs:289`), and the discovery payload's
  `DiscoveryLimits.MaxPageSize = PagingLimits.MaxPageSize`
  (`DiscoveryEndpoint.cs:50`). `DiscoveryEndpointTests.GetDiscovery_LimitsMaxPageSize_EqualsConstant`
  asserts the advertised limit equals the const at runtime as a belt-and-suspenders
  check. `AuditLogQuery.[Range(1, 200)]` correctly remains a separate literal as
  declared in the brief.
- **Bucket-B boundary is held.** `sort=downloads`, `sort=vulnerable`, and
  `sort=verified` all return 400 InvalidRequest via the model binder
  (`SearchV2SortTests.Sort_Downloads_Returns400_WithInvalidRequestBody` etc.).
  `SearchQuery` exposes no `vulnerable`/`verified`/`provenance` properties
  (`SearchQuery_DoesNotExposeBucketBFilterFields`). Discovery's Bucket-B flags are
  pinned `false` (`GetDiscovery_BucketBFlags_ArePinnedFalse`). No new DB columns
  added — `license` derives from existing `PackageRecord.License` and `ownerCount`
  from a correlated-subquery on `PackageRoleEntry`.
- **Pagination unification is wire-correct.** `SearchResponse`/`AuditLogResponse`
  are deleted; both controllers return `PagedResponse<T>` with key `"items"`;
  `SearchControllerTests` and `AuditLogControllerTests` explicitly assert
  `items` is present AND `packages`/`entries` are absent. The closed generic
  `PagedResponse<PackageSummaryResponse>` is registered in `CliJsonContext` (line 54)
  and the CLI's `SearchCommand` reads `results.Items`. Per-endpoint pageSize caps
  remain on the query DTOs.
- **Conditional caching is RFC-correct in the common cases.** `ConditionalResponse`
  emits a weak ETag (`W/"<ticks>-<suffix>"`), uses
  `candidate.Compare(etag, useStrongComparison: false)` for `If-None-Match`,
  truncates `Last-Modified` to whole seconds for `If-Modified-Since`, and
  explicitly `DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc)` so SQLite-
  roundtripped timestamps stay UTC. The five behavior tests cover ETag-match-304,
  IMS-equal-304, stale-ETag-200, weak-prefix, and headers-on-200. The JTI
  revocation gate correctly bypasses `/api/v1/.well-known/*` (Startup.cs:382)
  so the discovery endpoint remains reachable with a revoked token, verified
  by `GetDiscovery_WithRevokedJwt_Returns200`.
- **CORS is off-by-default and inert.** `CorsConfig.Enabled = false` by default;
  `Startup.Configure` reads the flag from live `IConfiguration` before deciding to
  call `UseCors` (Startup.cs:359-366); `AddCors` runs unconditionally so test
  factories can replace the policy without re-wiring services. `CorsMiddlewareTests`
  covers both the disabled (no headers) and enabled (preflight + actual GET) paths.

Three findings remain, all narrow. None block correctness of the declared
acceptance criteria; the highest is a cache-staleness bug in `/versions` that
surfaces only under per-version deprecation flips.

---

## F01 — [MEDIUM] /versions ETag does not invalidate on version-level deprecate/undeprecate

**Status:** fixed
**Fixed in:** 07ad55e6
**Files:** `Stash.Registry/Http/ConditionalResponse.cs:62-65`, `Stash.Registry/Controllers/PackagesController.cs:191`, `Stash.Registry/Database/StashRegistryDatabase.cs:393-417`, `Stash.Registry/Services/DeprecationService.cs:66-90`
**Phase:** P3
**Commit:** 3f10f7de

### Observation

The `/versions` weak ETag is computed as
`W/"<package.UpdatedAt.Ticks>-<TotalCount>"`
(`ConditionalResponse.SetHeadersAndCheckNotModified` builds the suffix from the
controller's `result.TotalCount` argument in `PackagesController.GetVersions`).

But the per-version deprecation paths bypass both inputs:

- `StashRegistryDatabase.DeprecateVersionAsync` (lines 393-404) flips
  `record.Deprecated`, `record.DeprecationMessage`, `record.DeprecatedBy` on the
  `VersionRecord` and calls `SaveChangesAsync()`. It does **not** touch
  `PackageRecord.UpdatedAt`.
- `StashRegistryDatabase.UndeprecateVersionAsync` (lines 406-417) is identical.
- `DeprecationService.DeprecateVersionAsync` / `UndeprecateVersionAsync`
  (`DeprecationService.cs:66, 82`) just calls the DB method; no parent-package
  timestamp bump.

So a `PATCH /api/v1/packages/{scope}/{name}/{version}/deprecate` flips
`VersionRecord.Deprecated` and `DeprecationMessage` — both of which
`PackagesController.BuildVersionResponse` (lines 635-652) projects into every
`VersionDetailResponse` item on the `/versions` listing — without changing either
`package.UpdatedAt` or `TotalCount`. A client whose `If-None-Match` carries the
pre-deprecation ETag will receive 304 with no body and continue showing the stale
`Deprecated=false`/`DeprecationMessage=null` data. The
`Cache-Control: public, max-age=60` headline doesn't bound this — once the client
starts revalidating, the stale ETag is what comes back on every subsequent request.

This is `/versions`-specific. `/readme` is unaffected: `UpdatePackageReadmeAsync`
(line 350) and `UpdatePackageLatestAsync` (line 338) both bump `UpdatedAt`, so
both `content` and `extractedFromVersion` flips invalidate the readme ETag.
Version add and unpublish are also covered — adds change `TotalCount`,
non-last unpublish calls `UpdatePackageLatestAsync` (`PackageService.cs:264`),
last-version unpublish changes `TotalCount` too. The only blind spot is
in-place per-version mutation.

### Why this matters

`/versions` is a brand-new endpoint and a public, paginated UI listing —
exactly the surface where conditional caching makes the most difference.
Shipping it with a cache-staleness bug means a Bucket-A consumer that follows
the standard `ETag`/`If-None-Match` revalidation pattern will durably
display incorrect deprecation status (`deprecated=false` for an actually-deprecated
version, or vice versa) for any version-level deprecation lifecycle change. The
broken contract is the brief's load-bearing P3 acceptance criterion that
"`ETag` (weak), `Last-Modified`, and `Cache-Control` headers are emitted on
200 responses; If-None-Match matching the current ETag returns 304" — the
text is satisfied, but the ETag's *meaning* (changes when the response body
would change) is silently violated for version-deprecation mutations.

### Suggested fix

Two viable shapes; either resolves the finding:

1. **Bump `package.UpdatedAt` on every version-level mutation.**
   Easiest: make `DeprecateVersionAsync` / `UndeprecateVersionAsync` in
   `StashRegistryDatabase` also set the parent `PackageRecord.UpdatedAt =
   DateTime.UtcNow` (mirroring how `UpdatePackageReadmeAsync` /
   `UpdatePackageLatestAsync` already work). Cheapest change; touches one file;
   parent-row write was already happening for every other content-changing path
   anyway. (Strictly, this would also cover any future `AddVersionAsync`
   /`DeleteVersionAsync` ETag dependency if the per-row count component were
   ever removed.)

2. **Fold version-row "fingerprint" into the ETag suffix.** Replace
   `<TotalCount>` with e.g. `<TotalCount-MAX(UpdatedAt-or-equivalent)>` on the
   version rows — a `MAX(...)` aggregate on the same `IQueryable` chain that
   already computes `TotalCount`. Avoids the parent-write but requires schema
   awareness (`VersionRecord` currently has no `UpdatedAt`; would need
   one added, or `MAX(Id)` if the table has an autoincrement surrogate). Cleaner
   semantically but a larger change.

Path (1) is the proportionate fix and matches the existing project pattern
(every parent-mutation path already bumps `UpdatedAt`; the version-deprecation
paths are the only outliers). Add a behavior test:
`DeprecateVersion_ThenIfNoneMatchPriorETag_Returns200` (and the mirror for
undeprecate) so the contract is locked.

A side note: the controller currently fetches both `result.TotalCount` *and*
`result.Items` before computing the conditional. When the conditional hits,
the `Items` list is materialized for nothing. The count alone is needed for
the ETag suffix; the items list isn't. If you touch `GetPackageVersionsAsync`
for fix (2) anyway, splitting it into a cheap `CountAsync()` first and only
materializing items when not-modified is false saves a query on every
revalidating cache hit. Not blocking — pure perf — and weakly worth folding
into the same change set since both touch the same chain.

### Verify

```
dotnet test --filter "FullyQualifiedName~VersionsEndpointTests"
# Plus a new explicit test along the lines of:
#   GetVersions_VersionDeprecated_InvalidatesETag()
#       — fetch /versions, capture ETag, PATCH /version/deprecate,
#         re-fetch with If-None-Match=<old ETag>, assert 200 + new ETag.
dotnet test --filter "FullyQualifiedName~ReadmeEndpointTests"
dotnet test
```

---

## F02 — [LOW] `keyword` search filter is vulnerable to LIKE-metacharacter / quote leakage

**Status:** fixed
**Fixed in:** bdf8bf12
**Files:** `Stash.Registry/Database/StashRegistryDatabase.cs:213-219`
**Phase:** P5
**Commit:** 0a6525bc

### Observation

`SearchPackagesAsync` builds the keyword-match LIKE pattern by string interpolation:

```csharp
if (!string.IsNullOrEmpty(keyword))
{
    string kw = keyword;
    queryable = queryable.Where(p =>
        p.Keywords != null &&
        EF.Functions.Like(p.Keywords, $"%\"{kw}\"%"));
}
```

The keyword value reaches the LIKE pattern unescaped. Three classes of input
break the intended exact-match semantics (no escape clause, no validation
attribute on `SearchQuery.keyword`):

- **`%`** in `keyword` becomes an unanchored wildcard in the pattern,
  matching arbitrary suffixes/prefixes mid-keyword. `keyword=foo%` matches every
  package whose keywords array contains a quoted token starting with `foo`.
- **`_`** matches any single character. `keyword=fo_` matches `"foo"`,
  `"fob"`, etc.
- **`"`** in `keyword` produces a malformed pattern (`%""<rest>""%`). EF Core will
  not reject it but the match becomes nonsense — likely zero results — when
  the user reasonably expects "no package has that literal in its keywords".

There is no SQL-injection risk — `EF.Functions.Like` parameterizes the second
argument — only filter-semantic drift. The behavior is undocumented (the brief
calls it "exact-match against one element of the package keywords JSON
array"), so a client that builds a keyword from user input cannot anticipate
the over/under-match.

### Why this matters

This is a Bucket-A search filter the future web client will expose to
end-users (the brief positions `keyword` as one of the four column-backed
filters that "are trivially useful to any UI"). A typed keyword like
`c++17` or `dev_tools` would silently match more than the user intended. The
fault scope is narrow — search misranking, not auth/visibility — so this is
LOW, not MEDIUM.

### Suggested fix

Either escape the LIKE metacharacters or validate the keyword grammar
client-side:

1. **Escape the three LIKE metacharacters in the runtime string** before
   interpolation, and append a `LIKE … ESCAPE '\'` clause. SQLite and
   PostgreSQL both support the `ESCAPE` clause:

   ```csharp
   string esc = keyword.Replace("\\", "\\\\")
                       .Replace("%", "\\%")
                       .Replace("_", "\\_")
                       .Replace("\"", "\\\"");   // and prevents the quote bug
   queryable = queryable.Where(p =>
       p.Keywords != null &&
       EF.Functions.Like(p.Keywords, $"%\"{esc}\"%", "\\"));
   ```

2. **Validate at the boundary** — add a `[RegularExpression(@"^[a-z0-9_-]{1,32}$")]`
   on `SearchQuery.keyword` (constraint matches the de-facto npm/pypi keyword
   grammar). Rejects pathological inputs with 400; no runtime escape required.
   Lighter touch, narrower contract, easier for a client to satisfy. The
   `RequestModelBindingMetaTests` infrastructure already enforces every
   Contracts-typed action parameter goes through model binding.

Either works; (2) is the smaller, more declarative change and matches the
existing pattern (`ScopeGrammarAttribute`, `TokenExpiryAttribute`,
`AdminUsernamePattern`). Pair with a test asserting `?keyword=foo%` returns
either 400 (option 2) or zero/literal-only matches (option 1).

### Verify

```
dotnet test --filter "FullyQualifiedName~SearchV2FiltersTests"
# Plus an explicit test:
#   Search_KeywordWithPercent_TreatedAsLiteral()  (option 1)
#   Search_KeywordWithPercent_Returns400()        (option 2)
dotnet test
```

---

## F03 — [LOW] `Updated`/`Published` sort orders have no tie-breaker — pagination can repeat or skip rows

**Status:** fixed
**Fixed in:** bdf8bf12
**Files:** `Stash.Registry/Database/StashRegistryDatabase.cs:257-267`, `Stash.Registry/Database/StashRegistryDatabase.cs:317-321`
**Phase:** P3, P5
**Commit:** 3f10f7de, 0a6525bc

### Observation

The new sort orders use a single ordering column with no secondary tie-breaker:

```csharp
// SearchPackagesAsync (P5)
var rowsQuery = sort switch
{
    PackageSortOrder.Name => queryable.OrderBy(p => p.Name),
    PackageSortOrder.Updated => queryable.OrderByDescending(p => p.UpdatedAt),
    PackageSortOrder.Published => queryable.OrderByDescending(p => p.CreatedAt),
    _ => queryable.OrderBy(p => p.Name),                             // Relevance
};

// GetPackageVersionsAsync (P3)
var items = await queryable
    .OrderByDescending(v => v.PublishedAt)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

When two or more rows share the sort key (two packages updated in the same
batch — same `UpdatedAt` second/tick; two versions published from the same
manifest in the same second), SQLite and PostgreSQL are both free to order the
ties arbitrarily and need not order them consistently across separate queries.
A client paging through results (page 1, page 2, page 3) can therefore see the
same row twice or miss a row entirely on the boundary between pages — the
classic "unstable sort + offset paging" pitfall. `OrderBy(p => p.Name)` is
already stable because `Name` is the primary key (uniqueness guarantees no ties),
so the `Name`/`Relevance` paths are unaffected.

### Why this matters

The web client the brief is unblocking is the consumer most likely to exercise
non-Name sorts (a "Recently Updated" or "Recently Published" view) and the most
sensitive to "this row jumped around as I clicked Next page." Both endpoints
are paginated and both expose the unstable orderings. Same-second collisions
are unusual but reachable — a bulk-publish script publishing several versions
into a single package in a tight loop, a publish-then-deprecate sequence
landing in one second's tick, etc.

### Suggested fix

Add a deterministic secondary key on every non-unique ordering. Cheapest and
correct:

```csharp
// SearchPackagesAsync
PackageSortOrder.Updated   => queryable.OrderByDescending(p => p.UpdatedAt)
                                       .ThenBy(p => p.Name),
PackageSortOrder.Published => queryable.OrderByDescending(p => p.CreatedAt)
                                       .ThenBy(p => p.Name),
// (Name/Relevance paths already stable on the Name primary key)

// GetPackageVersionsAsync
var items = await queryable
    .OrderByDescending(v => v.PublishedAt)
    .ThenByDescending(v => v.Version)   // or .ThenBy(v => v.Version)
    .Skip(…)
    .Take(…)
    .ToListAsync();
```

Adding `ThenBy(p.Name)` / `ThenBy(v.Version)` makes every ordering stable
across queries because the secondary key is part of the unique row identity.
Verify with a test that publishes 3 versions sharing the same `PublishedAt`
(easy: stamp them manually in the seed helper) and asserts they appear in a
fixed order on both page 1 and page 2.

### Verify

```
dotnet test --filter "FullyQualifiedName~VersionsEndpointTests|FullyQualifiedName~SearchV2SortTests"
# Plus explicit ties tests:
#   GetVersions_TiedPublishedAt_DeterministicOrder()
#   Search_TiedUpdatedAt_DeterministicOrder()
dotnet test
```

---
