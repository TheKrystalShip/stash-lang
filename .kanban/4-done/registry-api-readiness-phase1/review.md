# Registry API Readiness — Phase 1 (web-client prerequisites) — Review

> Produced by `/feature-review`. One finding per H2 section.

**Scope reviewed:** commits `d251d128..4edb1e9a` on branch `feature/registry-api-readiness-phase1` (worktree `stash-registry-api-readiness-phase1`)
**Brief:** ../brief.md
**Generated:** 2026-06-04
**Review pass:** 2 (final)

---

## Summary

**This is the second and final review pass.** Pass 1 (`b5fb0430`) raised three
findings — one MEDIUM (F01) and two LOW (F02, F03). All three have been resolved
by commits `07ad55e6` (F01) and `bdf8bf12` (F02, F03), and this pass verified
each fix against its observation, suggested fix, and the supporting tests.

**Verdict — zero new findings.** The feature is ready for `/done`.

### F01 verification — fixed

`StashRegistryDatabase.DeprecateVersionAsync` (lines 405-419) and
`UndeprecateVersionAsync` (lines 422-434) now load the parent `PackageRecord`
via `_context.Packages.FindAsync(name)` and set `package.UpdatedAt =
DateTime.UtcNow` **before the same** `await _context.SaveChangesAsync()` that
persists the version-row mutation. Both writes land in a single EF Core
change-tracking flush — there is no split-transaction window where the
version row could persist with the package timestamp stale. The pattern
mirrors `DeprecatePackageAsync` / `UndeprecatePackageAsync` (lines 376-402)
which already worked this way, so the version paths are now consistent with
the package paths.

The `if (package != null)` guard is appropriate — the version mutation is
already inside `if (record != null)`, and a version cannot exist without a
parent package row given the FK, but the null guard keeps the code
defensive at minimal cost.

Two new tests in `VersionsEndpointTests`
(`DeprecateVersion_ThenIfNoneMatchPriorETag_Returns200WithDeprecatedState`,
`UndeprecateVersion_ThenIfNoneMatchPriorETag_Returns200WithUndeprecatedState`)
exercise the full HTTP pipeline. They explicitly assert:

1. `Assert.NotEqual(oldEtag, newEtag)` — the load-bearing invalidation
   property, not just `200 OK`.
2. The response body reflects the new deprecation state
   (`deprecated == true` after PATCH, `deprecated == false` after DELETE).
3. The pre-deprecation ETag does not produce a 304.

The audit trail and the existing package-level `Deprecate{,Undeprecate}PackageAsync`
paths are untouched — F01's fix did not regress them.

### F02 verification — fixed

`SearchPackagesAsync` (lines 217-227) escapes the keyword input before
interpolation and passes `ESCAPE '\'` as the third argument to
`EF.Functions.Like`:

```csharp
string esc = keyword
    .Replace("\\", "\\\\")
    .Replace("%", "\\%")
    .Replace("_", "\\_")
    .Replace("\"", "\\\"");
queryable = queryable.Where(p =>
    p.Keywords != null &&
    EF.Functions.Like(p.Keywords, $"%\"{esc}\"%", "\\"));
```

Critical correctness points all hold:

- **Backslash escaped first** — if `%` were escaped before `\`, the `\%`
  produced by step 2 would be re-escaped to `\\%` by step 1 (which would
  then mean "literal backslash, then any-suffix wildcard"). The current
  order `\\` → `%` → `_` → `"` is correct.
- **The `ESCAPE` clause is wired** — the literal `"\\"` (= single
  backslash) is passed as the third argument to `EF.Functions.Like`,
  matching the escape character used in the input transformation. SQLite
  and PostgreSQL both honor this clause; EF Core's `Like` translator emits
  it transparently.
- **`"` escape was added correctly** — a quote in the keyword no longer
  produces the malformed pattern `%""<rest>""%`; it is escaped to a literal
  backslash-quote, which the storage layer (JSON-array text) treats as a
  literal character match.

The test `Search_KeywordWithPercent_TreatedAsLiteral` seeds two packages
(`pkg-percent` keyword `"c%"`, `pkg-cpp` keyword `"cpp"`) and queries
`?keyword=c%25` (URL-encoded `c%`). It asserts `items.GetArrayLength() == 1`
and the matched name is `pkg-percent`. Without the fix, the unescaped `%`
wildcard would also match `cpp` → totalCount == 2; with the fix only the
literal `c%` row matches. The test genuinely separates pre- and post-fix
behavior.

I confirmed no other interpolated-LIKE sites in `SearchPackagesAsync` need
the same fix:

- `license` filter (line 232-234) is `p.License == lic` — pure equality, no LIKE.
- `owner` filter (line 245-254) is `r.PrincipalId == ownerName` — equality.
- `deprecated` filter (line 237-241) is `p.Deprecated == dep` — equality.

(The `q` free-text search at lines 153-156 is a LIKE pattern but with
intentional substring semantics — fuzzy contains-match is the design, not
the exact-match semantics F02 was about. Out of scope for F02 per the
user-provided framing, and pre-existing behavior unchanged by this fix.)

### F03 verification — fixed

Two tie-breakers added, both producing total deterministic orderings:

**`SearchPackagesAsync` (lines 269-274):**

```csharp
PackageSortOrder.Updated => queryable
    .OrderByDescending(p => p.UpdatedAt)
    .ThenBy(p => p.Name),
PackageSortOrder.Published => queryable
    .OrderByDescending(p => p.CreatedAt)
    .ThenBy(p => p.Name),
```

`PackageRecord.Name` is the primary key (`HasKey(e => e.Name)`, confirmed in
`RegistryDbContext.OnModelCreating` line 77), so `Name` is unique. Adding it
as the secondary key makes the composite key unique → the order is total.
`Name` direction is reviewer judgment; ASC matches the conventional alphabetical
display tie-break used elsewhere in this codebase and is uncontroversial.

`Name` and `Relevance` paths are correctly left untouched — both already
order by `Name` (the unique PK), so they are already total.

**`GetPackageVersionsAsync` (line 328-329):**

```csharp
var items = await queryable
    .OrderByDescending(v => v.PublishedAt)
    .ThenByDescending(v => v.Version)
    ...
```

`VersionRecord` primary key is `(PackageName, Version)` (RegistryDbContext.cs:106).
`GetPackageVersionsAsync` filters `_context.Versions.Where(v => v.PackageName == name)`,
so within the filtered result set `Version` is unique → the composite
ordering is total. `Version DESC` matches the primary `PublishedAt DESC`
direction (newer-version-first when timestamps tie), a sensible display
choice; the column is a string so the lexical compare is not semver-aware,
but determinism — not semver correctness — is what F03 demanded, and the
pagination invariant (no skip, no repeat) holds.

Three new tests cover all three tied-ordering code paths
(`Sort_Updated_TiedTimestamps_DeterministicOrder`,
`Sort_Published_TiedTimestamps_DeterministicOrder`,
`GetVersions_TiedPublishedAt_DeterministicOrder`). All three:

1. Stamp identical timestamps (`tiedAt`) on every row.
2. Insert rows in **non-sorted** order (e.g. `c, a, b` for names; `3.0.0,
   1.0.0, 2.0.0` for versions) to detect any implicit rowid / insertion
   ordering.
3. Fetch page 1 (`pageSize=2`) AND page 2 (`pageSize=2`), asserting fixed
   cross-page ordering — i.e. they prove the full pagination guarantee, not
   just first-page determinism.

### Pass-1 areas re-confirmed

Re-confirmed clean (full review in the pass-1 commit history `b5fb0430`):
visibility chokepoint reuse on `/versions` and `/readme`; P2
`[RegistryAuthorize]` Detect-guard floor + fail-path fixture;
`PagingLimits.MaxPageSize = 100` single-source-of-truth across `SearchQuery`,
`VersionsQuery`, and `DiscoveryEndpoint`; Bucket-A/B boundary held (no new
DB columns; Bucket-B `sort` values rejected with 400; Discovery flags pinned
false); pagination unification (`PagedResponse<T>` with `items` key; CLI
`SearchCommand` reads `results.Items`); conditional caching (weak ETag, IMS
truncation, UTC kind specification); CORS disabled-by-default and inert.

### Baseline

Baseline `dotnet test` was green at the time of this pass (`failed=0
passed=13344 skipped=6` — the 6 skips are pre-existing `[Fact(Skip=…)]`
quarantines unrelated to this feature). The new tests added by the two
fix commits (3 in F01's commit, 4 in F02/F03's commit) are included in
that green count.

---

(No findings filed in pass 2.)
