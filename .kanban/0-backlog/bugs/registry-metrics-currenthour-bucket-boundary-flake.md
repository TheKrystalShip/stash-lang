# Metrics "current hour" endpoint tests flake when run in the first 5 minutes of an hour

**Status:** Backlog — Bug
**Created:** 2026-06-06
**Discovery context:** Surfaced by the orchestrator during the `registry-audit-log-v2` autopilot, review **pass-2** full `dotnet test` baseline. The baseline ran moments after the 2026-06-06 00:00 UTC day boundary and reported 2 failures; both are download-metrics endpoint tests, unrelated to the audit feature (which touches no metrics code). Isolated re-runs were green (2/2 and 12/12 for the full families), confirming a time-of-day flake, not a regression.

---

## Problem

Two download-metrics endpoint tests fail non-deterministically when the test process happens to run within the **first ~5 minutes of any clock hour** (UTC):

- `Stash.Tests.Registry.Metrics.PackageMetricsEndpointTests.GetPackageMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour`
- `Stash.Tests.Registry.Metrics.VersionMetricsEndpointTests.GetVersionMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour`

The symptom is a full-suite `dotnet test` reporting `Failed: 2` (exit 1) even though the audit/registry code under change is correct. Because it is wall-clock-dependent, the same suite passes on the next run — masking real failures behind "just re-run it" and, conversely, occasionally red-flagging an otherwise-green CI/gate run (~5/60 ≈ 8% of runs).

## Reproduction

The test seeds a raw download event at `DateTime.UtcNow.AddMinutes(-5)` and then asserts that event is counted in the **current-hour** open bucket. When "now" is within the first 5 minutes of an hour, `UtcNow.AddMinutes(-5)` lands in the *previous* hour's bucket, so the current-hour total does not include it and the assertion fails.

```bash
# Deterministic repro — fake the clock to 3 minutes past the hour (requires a
# clock seam; today the test uses real DateTime.UtcNow, so the cheap repro is timing):
#   run the two tests in the 00..05 / 01..05 / ... minute-of-hour window:
dotnet test Stash.Tests/Stash.Tests.csproj \
  --filter "FullyQualifiedName~PackageMetricsEndpointTests.GetPackageMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour|FullyQualifiedName~VersionMetricsEndpointTests.GetVersionMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour"
# FAILS when minute-of-hour < 5; PASSES otherwise.
```

## Blast radius

- **Test-suite reliability only — not a product bug.** The metrics counting code is correct; only the *tests'* seed-timestamp choice is fragile. No registry user is affected.
- **CI / checkpoint gates:** any full-suite gate (`/feature-review` baseline, `/done` final_verify, `worktree-finish` post-merge verify) that happens to run in the first 5 minutes of an hour will go red on these two tests, stalling an otherwise-green feature. The autopilot has to special-case "is this the known metrics flake?" on every full-suite red — exactly the kind of judgement a deterministic suite should not require.
- Latent-but-recurring: it will keep tripping ~8% of full-suite runs until fixed.

## Root cause

`Stash.Tests/Registry/Metrics/PackageMetricsEndpointTests.cs:276` (and the `VersionMetricsEndpointTests` equivalent) seed the open-bucket event with a wall-clock-relative timestamp:

```csharp
await SeedRawEventAsync(ctx.Factory, $"@{username}/openbkpkg", "1.0.0", DateTime.UtcNow.AddMinutes(-5));
```

`-5 minutes` is not guaranteed to stay inside the current hour. The endpoint buckets by hour, so a seed that crosses the hour boundary is attributed to the wrong (previous) bucket, and the "current hour total includes it" assertion fails. The same anti-pattern (`DateTime.UtcNow.AddHours(-2)` at lines 219/248) is safe only because those assertions don't depend on same-hour membership.

## Suggested fix

- **(A) Seed inside the current hour deterministically** — replace `UtcNow.AddMinutes(-5)` with a timestamp pinned to the current hour, e.g. `var hourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc); SeedRawEventAsync(..., hourStart.AddMinutes(1));` (or `now` itself if "current hour" is the only requirement). Cheap, local, no production change. **Recommended.**
- **(B) Introduce a clock seam** — inject an `IClock`/`TimeProvider` (production already has access to `TimeProvider` in .NET 10) into the metrics rollup + endpoint, and have the test pin it to a fixed instant. More invasive but kills a whole class of time-boundary flakes across metrics + the new audit retention/tamper-evidence tests too. Worth considering if more time-dependent flakes appear.

Recommend **(A)** as the immediate fix; keep **(B)** in mind if download-metrics or audit grow more clock-coupled tests.

## Verification

```bash
# After (A): force the bad window in a loop, must stay green:
for i in $(seq 1 5); do \
  dotnet test Stash.Tests/Stash.Tests.csproj \
    --filter "FullyQualifiedName~PackageMetricsEndpointTests.GetPackageMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour|FullyQualifiedName~VersionMetricsEndpointTests.GetVersionMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour"; \
done
# Before the fix: fails iff run in minute-of-hour < 5. After: always green.
```

The existing 12 metrics-endpoint tests must continue to pass.

## Related

- Surfaced during `registry-audit-log-v2` (self-hosted-registry P3) autopilot, pass-2 baseline; the audit feature itself is clean (the failures are in a disjoint test family).
- Bug lives in download-metrics test code (`registry-download-metrics`, self-hosted-registry P2). Likely introduced with those tests.
- Relates to the project memory `feedback-no-useless-skips` (flakes are debt to eliminate, not tolerate) — this should be fixed, not quarantined.

## Recurrence — 2026-06-07 (`language-standard-values`, `language-standard` milestone)

The same two tests flaked again during the `language-standard-values` `/done` `final_verify` (a §Values & Types feature touching **no** metrics code) — confirming this is a recurring, cross-feature gate-stall, not a one-off. Both passed 2/2 in isolation; `promote-done` was re-run once and went green (14383 passed / 0 failed / 6 skipped).

Two observations from this sighting that refine the report above:
- The live seed offset is now **`DateTime.UtcNow.AddMinutes(-3)`** (e.g. `VersionMetricsEndpointTests.cs:227`), not `-5`. So the failure window is the **first ~3 minutes** of a clock hour, not 5 — the suggested fix (A: pin the seed inside the current hour) is unchanged and still correct; only the window width differs.
- This is the **second distinct feature** (`registry-audit-log-v2`, then `language-standard-values`) whose hard gate this flake has stalled. It directly contradicts the `CLAUDE.md` "the suite is green, there are no flakies" invariant for these two tests — raising the priority of fix (A).

A second stub (`flaky-metrics-openbucket-currenthour-clock-boundary.md`) was filed for this recurrence before the duplicate was noticed; it has been folded into this canonical record and removed.
