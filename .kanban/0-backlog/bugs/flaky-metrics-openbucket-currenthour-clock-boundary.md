# Flaky: `*MetricsEndpointTests.Get*Metrics_OpenBucketRawAdded_TotalIncludesCurrentHour` race the clock-hour boundary

**Status:** Backlog — Bug (flaky test; breaks the green-suite invariant)
**Created:** 2026-06-07
**Discovery context:** Surfaced repeatedly during full `dotnet test` runs for the `language-standard-values` feature (`language-standard` milestone). The full suite failed intermittently with 1–2 failures, always confined to this test pair; both pass deterministically when run in isolation. The feature itself is unrelated (registry download-metrics subsystem vs §Values & Types). Filed here because it intermittently blocks **every** feature's `final_verify`/review baseline, contradicting the `CLAUDE.md` "the suite is green, there are no flakies" invariant — which is now false for these two tests.

---

## Problem

`Stash.Tests/Registry/Metrics/VersionMetricsEndpointTests.GetVersionMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour` (line 215) and the sibling `PackageMetricsEndpointTests.GetPackageMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour` (line 264) fail non-deterministically under full-suite parallel load. Observed this session: 1 failure in one full run, 0 in another, 2 in a third (~intermittent). Each passes 2/2 in isolation.

## Reproduction

Not reliably reproducible on demand (timing-dependent), but the window is deterministic: the failure occurs when the test executes during roughly the **first 3 minutes of any UTC clock hour**, and is made more likely by parallel-load execution drift across the boundary.

```bash
# Passes in isolation (always):
dotnet test Stash.Tests/Stash.Tests.csproj --filter "FullyQualifiedName~VersionMetricsEndpointTests.GetVersionMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour|FullyQualifiedName~PackageMetricsEndpointTests.GetPackageMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour"

# Flakes intermittently under full-suite parallel load:
dotnet test    # ~0–2 of these two fail, depending on wall-clock + load
```

## Blast radius

- **Every feature's `final_verify` / review baseline** can fail spuriously on these two tests, stalling `/done` and the autopilot's review passes. This is the highest-impact symptom: it makes a hard gate non-deterministic.
- No production impact — the metrics-bucketing code is correct; only the test's seed-timestamp assumption is fragile.

## Root cause

`VersionMetricsEndpointTests.cs:227` (and the Package sibling) seed a raw download event at `DateTime.UtcNow.AddMinutes(-3)`, then assert the **current-hour** bucket total includes it. Hour buckets are computed by truncating to the clock hour. When `UtcNow` is within the first 3 minutes of an hour (`HH:00`–`HH:03`), `UtcNow.AddMinutes(-3)` lands in the **previous** hour (`HH-1:5x`), so the seeded event is bucketed into the prior hour and the "current hour" total does not include it → assertion fails. Parallel-load execution delay between the `UtcNow` read and the bucketing widens the effective window.

## Suggested fix

Make the seed timestamp unambiguously inside the current hour rather than `now − 3min`. Options (pick one):
1. **Controllable clock (preferred):** inject the clock the metrics endpoint/test uses and seed at a fixed offset from a frozen `now`, so the test is independent of wall-clock position.
2. **Seed at the current-hour floor:** `var hourStart = new DateTime(UtcNow.Year, ..., UtcNow.Hour, 0, 0, DateTimeKind.Utc); SeedRawEventAsync(..., hourStart.AddMinutes(1));` — guaranteed to be in the current hour regardless of where in the hour the test runs (still has a ~1-minute boundary risk at `HH:00:00`; the controllable-clock fix is cleaner).
3. If neither is taken promptly, quarantine both with `[Fact(Skip = "flaky clock-boundary race; see flaky-metrics-openbucket-currenthour-clock-boundary.md")]` so the suite is deterministically green again (loses coverage — least preferred).

## Verification

After the fix, run the full suite several times across (or near) an hour boundary and confirm 0 failures, then confirm the two tests still assert the intended current-hour-inclusion behavior in isolation.

## Related

- `Stash.Tests/Registry/Metrics/VersionMetricsEndpointTests.cs:215` / `:227`
- `Stash.Tests/Registry/Metrics/PackageMetricsEndpointTests.cs:264`
- `Stash.Tests/CLAUDE.md` → "Parallelization — serialize process-global state" (related discipline, though this is a clock-boundary race more than a shared-state race)
- Surfaced during `language-standard-values` (`.kanban/4-done/language-standard-values/` once promoted)
