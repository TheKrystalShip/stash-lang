# Registry SQLite backend returns 500 on concurrent writes (no busy_timeout)

**Status:** Backlog — Bug
**Created:** 2026-05-30
**Discovery context:** Surfaced while fixing a flaky registry concurrency test during `registry-authz-pipeline` P4. The flaky test (`RegistryAuthzAtomicCreateTests`/`AtomicClaimRaceTests`) was primarily a test-harness artifact (single shared `SqliteConnection` across concurrent requests, now fixed), but investigating it exposed that the *production* SQLite connection sets no busy-wait timeout — so the same class of 500 is reachable in a real SQLite deployment under concurrent writes.

---

## Problem

The registry's production EF Core connection for the SQLite backend is configured as `options.UseSqlite($"Data Source={_config.Database.Path}")` (`Stash.Registry/Startup.cs:134`) with no `busy_timeout` / command timeout tuning. SQLite permits only one writer at a time; a second concurrent write transaction receives `SQLITE_BUSY`. Without a busy timeout, Microsoft.Data.Sqlite surfaces this immediately as an exception rather than waiting for the lock, which the request pipeline turns into an HTTP **500**.

This means two clients publishing/claiming into the registry at the same moment, on a SQLite-backed deployment, can get a 500 instead of the intended `201`/`409` (one winner, the rest cleanly denied). SQLite is the **default** backend (`appsettings.json` → `Database.Type = "sqlite"`), so this is the out-of-the-box configuration.

## Reproduction

Deterministic at the unit level once the test harness uses independent connections (shared-cache in-memory): N concurrent `PUT /api/v1/packages/{scope}/{name}` (first-publish) or `POST /api/v1/scopes` against a SQLite-backed registry with no busy timeout intermittently yields a 500 carrying a `SqliteException` "database is locked" (SQLITE_BUSY), instead of exactly one 201 + the rest 409/403.

The `registry-authz-pipeline` concurrency tests assert exactly this invariant ("zero 500s"); they now pass because the *test* connection string sets `Default Timeout=30` (busy-wait). Production does not set an equivalent.

## Blast radius

- **Latent today**, real under load: any self-hosted registry on the default SQLite backend with concurrent publishers (CI fan-out, a team publishing simultaneously). PostgreSQL deployments are **unaffected** — Npgsql/Postgres handle concurrent writers with row/transaction locking, no SQLITE_BUSY.
- Compounds with adoption: the more concurrent the publish traffic, the more frequent the spurious 500s. The atomicity design (insert-then-handle-unique-violation, D20) is correct; the gap is purely that lock contention is not absorbed by a busy-wait.

## Root cause

`Stash.Registry/Startup.cs:134` — `options.UseSqlite($"Data Source={_config.Database.Path}")` sets no busy timeout. A blocked SQLite writer therefore fails fast with `SQLITE_BUSY` instead of waiting for the write lock to free, and the exception escapes as a 500. (The test harness previously masked *and* compounded this with a single shared `SqliteConnection` reused across concurrent requests — that part is a test bug and is fixed in `RegistryAuthzFactory.CreateConcurrent`.)

## Suggested fix

- (A) **Set a busy timeout on the SQLite connection** — append `;Default Timeout=30` (Microsoft.Data.Sqlite command timeout → busy-wait) to the production connection string, or execute `PRAGMA busy_timeout=30000;` on open. Sketch: smallest change, mirrors what the test harness now does, makes concurrent writers wait instead of 500. Trade-off: a pathologically long lock turns a fast 500 into a slow request up to the timeout.
- (B) **Enable WAL journal mode** (`PRAGMA journal_mode=WAL;`) in addition to (A) — allows concurrent readers alongside one writer, reducing contention. Trade-off: WAL has its own file-management and NFS caveats; more than the minimal fix.
- (C) **Catch `SqliteException` SQLITE_BUSY in the write path and retry/translate to 409** — explicit handling. Trade-off: scatters SQLite-specific logic into business code; (A) is cleaner.

Recommend **(A)**, optionally **(B)** for read-heavy deployments. Keep this out of `registry-authz-pipeline` — it is a DB-robustness concern, not an authorization concern, and PostgreSQL is unaffected.

## Verification

```bash
# A production-faithful concurrency test on a FILE-backed SQLite registry (not the
# shared-cache in-memory harness) that issues N concurrent first-publish PUTs and
# asserts zero 500s. Must FAIL before the busy_timeout fix, PASS after.
dotnet test --filter "FullyQualifiedName~RegistrySqliteConcurrencyTests"
```

Cross-cutting checks that must continue to pass: `RegistryAuthzAtomicCreateTests`, `AtomicClaimRaceTests` (the in-memory harness suite), and the full `FullyQualifiedName~Registry` filter.

## Quarantined reproducer tests

Three tests assert "zero 500s under N concurrent **first-publishes**" (the doubly-write-contended claim+create+store path) and reproduce this gap ~1-in-3 under full-suite load on SQLite. They are tagged `[Trait("Category", "SqliteConcurrencyStress")]` and excluded from the default gate (`&Category!=SqliteConcurrencyStress` in `final_verify` and P7 verify), exactly as `BasePathIntegrationTests` is excluded as an env-flaky:

- `AtomicClaimRaceTests.OpenMode_ConcurrentFirstPublish_ExactlyOneScopeRowCreated`
- `RegistryAuthzAtomicCreateTests.AtomicCreate_ConcurrentFirstPublish_ExactlyOnePackageRow_ZeroFiveHundreds`
- `RegistryAuthzAtomicityConformanceTests.AtomicityConformance_ConcurrentFirstPublish_ExactlyOnePackageRow`

They remain runnable on demand (`dotnet test --filter "Category=SqliteConcurrencyStress"`) and should turn reliably green once the busy_timeout fix lands — that is their acceptance check. The atomicity *invariant* (exactly-one-row, clean denials) is still gated by `RegistryAuthzAtomicCreateTests.Schema_PackageName_IsUniqueConstraint` and the single-write claim-race tests (`ClaimScope_ConcurrentRequests`), which are stable.

Harness note: switching `RegistryAuthzFactory.CreateConcurrent` from shared-cache in-memory (which raised `SQLITE_LOCKED_SHAREDCACHE`, not serviced by busy_timeout) to a temp **file** DB reduced but did not eliminate the flake under heavy parallel load — consistent with this being a real SQLite write-contention gap, not a pure test artifact.

## Related

- Test harness: `RegistryAuthzFactory.CreateConcurrent` (temp-file SQLite, per-request connections, `Default Timeout` busy-wait) + `RegistryConcurrencyCollection`.
- Atomicity design: `registry-authz-pipeline` brief, D20 (insert-then-handle-unique-violation) — correct; this bug is orthogonal (the auto-claim path uses the atomic helper and denies cleanly; the 500 is transient infra contention, not a logic defect).
