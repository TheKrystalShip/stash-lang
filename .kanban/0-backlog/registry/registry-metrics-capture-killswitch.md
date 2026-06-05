# Registry Metrics Capture Kill-Switch

**Status:** Open — design decision deferred (not scheduled)
**Created:** 2026-06-05
**Discovery context:** F03 of `registry-download-metrics` review. The `MetricsRawConfig.RetentionDays`
docstring promised that `RetentionDays=0` disables raw capture entirely, but the implementation only
no-ops the nightly retention sweep when `RetentionDays <= 0`. Separately, `MetricsConfig.Enabled`
exists (defaults `true`) but no code reads it — it is a dead knob. Both have been corrected in their
docstrings (see fix commit for F03), but no working capture kill-switch exists.

## Problem

No working configuration disables raw download capture.

- `RetentionDays=0` disables only the nightly retention sweep (now documented accurately). Raw events
  are still captured and will accumulate indefinitely until a positive `RetentionDays` is set.
- `Metrics.Enabled` exists on `MetricsConfig` and defaults to `true`, but nothing in the codebase
  reads this property — it is completely inert. Setting it to `false` in `appsettings.json` has no
  effect on capture, rollup, or retention.

A privacy-conscious operator who wants downloads counted but raw IP/user-agent rows never persisted to
disk has no working knob. (`IpMode=off` gives partial relief — it counts without storing the raw IP —
but raw `download_events` rows still accumulate.)

## Options

**(A) Wire `Metrics.Enabled` as the master capture switch (recommended).** In
`PackagesController.DownloadVersion`, skip the `Response.OnCompleted` registration entirely when
`_metricsConfig.Enabled == false`. Optionally also gate the background rollup and retention passes on
`Enabled`. This makes the existing knob truthful with minimal new surface. The `Startup.cs` conditional
registration of the queue / hosted service / IpHasher is a nice follow-up but not strictly required for
the kill-switch to work. Net-new behavior — needs its own spec and tests.

**(B) Give `RetentionDays=0` "don't capture" semantics.** Repurpose the `RetentionDays=0` value to
mean "do not write raw events at all" (i.e. gate capture on `RetentionDays > 0`). This collapses two
knobs into one but conflates "how long to keep" with "whether to capture" — conceptually confusing and
risks surprising operators who set `RetentionDays=0` to mean "sweep immediately, not disable". Not
recommended.

**(C) Decide no kill-switch is needed, remove `Metrics.Enabled`.** If the policy is "download capture
is always on; operators tune IpMode for privacy," remove the dead `Enabled` property so the config
surface doesn't lie. Privacy-conscious deployments continue to use `IpMode=off` as their only tool.
This is the simplest path if operational requirements don't demand a full disable.

Note: option (A) is the conventional design (a boolean enable/disable knob is idiomatic for optional
subsystems in this codebase), but it is net-new behavior that needs its own spec, integration test, and
brief-AC coverage — hence deferred rather than fixed inline in F03.

## Suggested fix / Verify

When implemented via option (A):

- Gate `Response.OnCompleted` registration in `PackagesController.DownloadVersion` on
  `_metricsConfig.Enabled == true`.
- Add a `DownloadCaptureSemanticsTests` row: publish a package, set `Enabled = false`, perform a
  successful download, assert `download_events` count is 0 (ZERO events enqueued).
- Verify with:
  ```
  dotnet test --filter "FullyQualifiedName~Registry.Metrics.DownloadCaptureSemantics"
  ```

## Related

- `registry-download-metrics` (self-hosted-registry P2) — the feature that introduced these knobs
- `Stash.Registry/Configuration/MetricsConfig.cs` — `MetricsConfig.Enabled` and `MetricsRawConfig.RetentionDays`
- `Stash.Registry/Controllers/PackagesController.cs` — `DownloadVersion` unconditional enqueue path
- `Stash.Registry/Services/Metrics/MetricsBackgroundService.cs` — retention sweep no-op guard
- Decision D8 (no synchronous DB write on hot path) and D11 (IP handling pipeline) from the brief
