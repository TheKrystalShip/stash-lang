# Test Suite Green-Up — Skips + Flakies + Hidden-Red Stabilization

*(formerly "Flaky Test Classes — Root Cause Stabilization")*

**Status:** Backlog — Bug / Quality
**Created:** 2026-05-19
**Refreshed:** 2026-06-01 — full source-level re-investigation, a **finder→refuter pass**, an **unfiltered full-suite run**, and **empirical un-skip runs** of the skipped tests. Conclusions below are marked `[observed]` (ran it), `[verified]` (read the code), or `[hypothesized]` (plausible, not yet confirmed).

---

## Goal

Make **unfiltered `dotnet test` green with ZERO `Skip` attributes remaining.** A skipped test is worse than no test — it gives false coverage comfort. Every feature today repeats a growing `final_verify` exclusion filter to dodge these; that boilerplate is the recurring pain, and — as this investigation proved — it has been **hiding real, deterministic breakage**.

**done_when:** `dotnet test` (no `--filter`) reports 0 failed, 0 skipped; the per-feature exclusion filters in `plan.yaml` templates are deleted.

---

## Empirical baseline (2026-06-01, unfiltered run)

```
Total: 12748   Passed: 12592   Failed: 44   Skipped: 112   (~42s)
```

**The 44 failures are NOT the skipped tests** — they are filter-excluded classes that still *run* (no `[Fact(Skip)]`). They break down as:

| Class | Count | Isolation result | Verdict |
|---|---|---|---|
| `DiffPackageTests` | 35 | **fails alone too** `[observed]` | **Deterministically RED** — hidden by exclusion |
| `AuthEndpointTests` | 3 | **passes alone (3/3)** `[observed]` | Parallel-flaky (shared-resource race) |
| `FirstRegistrationAdminTests` | 3 | **passes alone (3/3)** `[observed]` | Parallel-flaky (shared-resource race) |
| `BasePathIntegrationTests` | 2 | **fails alone too (2/2)** `[observed]` | **Deterministically RED** — hidden by exclusion |
| `AtomicClaimRaceTests` | 1 | quarantined | Group D — SQLite busy_timeout |

The **112 skips** are a separate axis, triaged below.

---

## ⚠ HEADLINE: exclusion masked two deterministically-broken classes

These are red **on their own**, not flaky. Nobody noticed because every feature filters them out:

### `DiffPackageTests` (35 failing) — `[observed]`, root cause `[verified]`
Every failure is `RuntimeError: Module does not export 'MARKER_INSERT'`. The `@stash/diff` example package's `lib/constants.stash` declares `const MARKER_INSERT = "+"` **without `export`**, and `lib/render.stash` does `import { MARKER_INSERT, … } from "constants.stash"`. After the **`exports-private-default`** feature made top-level declarations private-unless-exported, the dogfooded package was never updated → the named import now fails at runtime.
**Fix:** add `export` to the constants in `examples/packages/diff/lib/constants.stash` (and audit the rest of the package for other now-private exports). This is a **real regression in shipped example code** — the most important single finding here.

### `BasePathIntegrationTests` (2 failing) — `[observed]`
Fails in isolation with `SqliteException: SQLite Error 1: 'no such table: scopes'` at `StashRegistryDatabase.SeedSystemScopesAsync():587` — the harness seeds system scopes **before the schema is created** (missing `EnsureCreated`/migration in this fixture's setup). Under full load it *also* threw `DirectoryNotFoundException`, but the seed bug makes it red regardless.
**Fix:** correct the fixture's DB bootstrap so the schema exists before seeding.

---

## Skipped-test triage (112 skips)

### Bucket A — `args` namespace removal: **DELETE (86 tests)** `[verified]`
86 tests across 7 files are skipped `"args namespace removed in cli-arg-parsing; migrated by follow-up spec"` (ArgsBuildTests 44, DictArgParseTests 38, ArgsNamespaceTests 1, DapIntegrationTests 1, TypeInferenceTests 1, AliasHooksTests 1, AliasPersistenceTests 1). The `args` namespace was removed in the completed `cli-arg-parsing` feature (no deprecation, pre-1.0); `ArgsRemovalTests.cs` already asserts it's gone. The promised follow-up migration spec was **never filed**.

- **Coverage check passed:** the replacement `cli.*` API already has 200+ tests (`CliTryParseTests` 50, `CliSchemaBuilderTests` 40, `CliValidationPipelineTests` 31, `CliSubcommandTests` 23, `CliBuildTests` 21, `CliParseExitTests` 15, `CliArgvTests` 6, `CliHelpRenderTests` 12, …). Deleting the old `args.*` tests loses **no** semantic coverage. `[verified]`
- **DELETE:** `ArgsBuildTests.cs`, `DictArgParseTests.cs`, `ArgsNamespaceTests.cs` (test a deleted API; dict-schema shape is incompatible with the new typed `CliSchema`).
- **MIGRATE (1-line each):** the `args.parse` call in `DapIntegrationTests` and `TypeInferenceTests` fixtures → `cli.parse(cli.schema(...))`.
- **USER-DECISION:** `AliasHooksTests` / `AliasPersistenceTests` use `${args}` as an **alias-template variable**, possibly unrelated to the removed namespace — check whether `${args}` expansion still works; if so these are false-positive skips and just un-skip.

### Bucket B — stale skips: **just un-skip** `[observed]`
The bug was fixed; the skip was never removed. Running them is green today.
- `InterpreterTests.ForIn_ArrayMutation_DoesNotCrash` ("It hangs for some reason") — **passes in 200 ms** now; for-in snapshots the array correctly. *(`Timeout=` xUnit attr only works on async tests — rely on a wall timeout to guard the un-skip in CI.)*
- `FuzzHarnessTests.FuzzCorpus_PipelineOnAndOff_IdenticalOutput` — single test, exits 0 in isolation; the referenced `fuzz-host-crash-pipeline-on-off.md` is empty. The repo.md "host-process crash" claim is **stale**. Re-enable (or delete if the corpus comparison is no longer wanted) and drop the filter token.

### Bucket C — deterministically broken assertions: **fix the test** `[observed]`
- `TermBuiltInsTests.Width_ReturnsPositiveInt` ("Flaky as heck") — **not flaky; the assertion is inverted.** Line 251 is `Assert.False(width > 0)` but the test name promises a *positive* int and `term.width()` returns `80` when headless. Observed failure: `Assert.False() Expected False, Actual True`. **Fix:** change to `Assert.True(width > 0)` — it then passes deterministically (the `Console.WindowWidth` IOException is already caught → 80 fallback).

### Bucket D — genuinely flaky, pass in isolation: **trait-gate + event-sync** `[observed]`
These **passed when run alone** but flake under parallel/resource load — same family as Group 1 below.
- `NetBuiltInsTests` TCP/UDP (7 skips) — hardcoded ports 19876–19880, `time.sleep(0.1)`. `TcpSend_ReturnsByteCount` passed in isolation.
- `PackageInstallerTests.DownloadAndCache_…` — TOCTOU port race in `StartTestServer` (bind→`Stop()`→`Start()` gap).
- `FsWatchBuiltInsTests` (2 skips) — `time.sleep(0.8)` + OS event coalescing. `Watch_DebounceDifferentFiles_NoCoalescing` **failed** when run (`Assert.True Expected True, Actual False`) — confirms the coalescing assumption is fragile.
- `SysBuiltInsTests.OnSignal_SIGUSR1_HandlerInvoked` — real process-global `PosixSignalRegistration`; passed alone, clashes when two tests register the same signal in parallel.
- `CliExecutionTests.Stdin_SimplePrint_PrintsOutput` — spawns a `stash` subprocess; passed alone, flakes on stdout flush/buffering under load.
- `AsyncAwaitTests.AsyncFn_ParallelExecution_FasterThanSequential` — wall-clock `parallelElapsed * 2 < sequentialElapsed` assertion (`AsyncAwaitTests.cs:145`). `[verified]`
- `SafeShellInterpolationE2ETests` — runtime `if (OperatingSystem.IsWindows()) return;`, POSIX `printf`/`/bin/sh` dependency. `[verified]`

**Fix (one unit):** `[Trait("Category","RequiresNetwork"/"RequiresShell")]` gating, `ManualResetEventSlim`/`SemaphoreSlim` instead of fixed sleeps, gapless port allocation, distinct signals per test (or a serial collection), explicit flush in subprocess scripts. For the perf assertion, assert correctness not timing.

---

## Group D2 — SQLite write contention *(⚠ PRODUCTION BUG)*

`AtomicClaimRaceTests`, `RegistryAuthzAtomicCreateTests`, `RegistryAuthzAtomicityConformanceTests` (`[Trait("Category","SqliteConcurrencyStress")]`, `[Collection("RegistryConcurrency")]`) assert "zero 500s under N concurrent first-publishes." On SQLite this hits `SQLITE_BUSY` → 500 (~1-in-3 under load) because **`Stash.Registry/Startup.cs:134` sets no `busy_timeout`** on the production connection. A real self-hosted registry on the default backend can 500 under concurrent publishers; PostgreSQL is unaffected.
**Fix:** set `busy_timeout` (`;Default Timeout=30` / `PRAGMA busy_timeout`). Full analysis in `Registry SQLite backend returns 500 on concurrent writes (no busy_timeout).md`. **Disjoint subsystem (registry)** → parallelizable with the test-infra work.

---

## Cross-cutting hardening — process-global cwd serialization `[observed gap]`

Five test classes call `Directory.SetCurrentDirectory`, but **only `GlobExpansionTests` is in `[Collection("SystemCwdTests")]`** (DisableParallelization). The other four — `CliPackageCommandsTests`, `StashFormatTests`, `StashCheckTests`, `CrossPlatformTests` — mutate the **process-global cwd** while running in parallel with everything else.

- `FormatRunner_ExcludeGlob` (in `StashFormatTests`) is the documented cwd-flaky; it's simply **missing the collection attribute**.
- **Hypothesis** `[hypothesized]`: this is also why `AuthEndpointTests` / `FirstRegistrationAdminTests` flake — they resolve a relative `StashRegistry/` path and a racing `SetCurrentDirectory` (or a shared-fixed-dir create/delete race) pulls the rug out → `DirectoryNotFoundException`. **Not pinned**: a minimal `AuthEndpoint + StashFormatTests` pairing passed 80/80, so the trigger needs heavier parallel load. Report as "parallel shared-resource race, exact trigger unpinned."
- **Actionable regardless:** add `[Collection("SystemCwdTests")]` to all four un-serialized cwd-mutators (or make their cwd use absolute paths). This is a correct hardening whether or not it's THE registry-flake cause.

---

## DAP shared-state flake (already has a dedicated stub)

`NamespaceMembersDapTests.NamespaceExpansion_CliNamespace_ShowsMembersWithMemberType` — process-scoped `NamespaceMemberPayload._cachedValue` shared across VMs via `StdlibDefinitions._vmGlobalsCache`. **The `Freeze()` TOCTOU theory was refuted** (`Interlocked.CompareExchange` is sound). Fix per `Cached NamespaceMember Payload Shared Across VM Instances.md` (cache on VM context).

---

## Reclassify, don't fix

- `DiffPackageTests` is **not slow-and-fine** (the prior note was wrong) — it's broken; see headline. Once the `export` fix lands, consider `[Trait("Category","Slow")]` so it can be excluded from the *fast* loop by trait, never by namespace filter.

---

## Suggested sequencing

1. **Headline fixes (highest value — real breakage):** `@stash/diff` `export` fix (35 tests green); `BasePathIntegrationTests` DB-bootstrap fix (2 green).
2. **Bucket A delete/migrate (86 skips → 0):** biggest skip reduction, low risk (replacement coverage verified). File the never-filed follow-up as *this*.
3. **Bucket B + C (stale + inverted):** un-skip the hang & fuzz; fix the term-width assertion. Near-trivial.
4. **cwd serialization:** add the collection attribute to 4 classes; re-run full suite to see if the registry flakes vanish.
5. **Group D2 — SQLite busy_timeout:** product + test; registry subsystem, parallelizable.
6. **Bucket D / Group 1 — external-resource trait-gating + event-sync:** the largest test-infra unit.
7. **DAP payload cache:** the deepest VM-context refactor; do last.

## Out of scope

Test-infrastructure + two product fixes (`@stash/diff` export drift; registry `busy_timeout`). No language-semantics or bytecode changes — the `exports-private-default` semantics are *correct*; the diff package merely wasn't updated to them.
