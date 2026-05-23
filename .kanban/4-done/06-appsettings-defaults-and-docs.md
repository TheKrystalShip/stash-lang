# appsettings.json Defaults — Sensible Rate-Limit Values and Doc Alignment

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** High
> **Discovery context:** Stash package registry audit — findings **S1** and **S10**.

## Background

Two related defects in the registry's shipped configuration:

- **S1 — Rate-limit defaults are landmines.** `appsettings.json` ships with rate limiting **disabled** (`Enabled: false`), but the `MaxAttempts` and `WindowSeconds` values are `0`. If a self-hoster flips `Enabled: true` without rewriting the other two values (the natural thing to do — "I want rate limiting on"), the rate-limit middleware divides by `WindowSeconds` to compute requests-per-second and either throws a `DivideByZeroException` or the comparison `attempts >= MaxAttempts` is always true (every request blocked) depending on which value is hit first. Both modes break the registry on a single config flip.
- **S10 — Config section prefix mismatch between docs and binding.** The README documents the config section as one prefix (e.g., `"Stash:Registry:RateLimit"` or similar), but the actual `IOptions<>` binding in `Program.cs`/`Startup.cs` uses a different prefix (e.g., `"RateLimit"` at the root). Self-hosters following the docs configure a section that the app silently ignores, and wonder why their settings have no effect.

The audit found the actual binding key — the docs are wrong, not the code (or vice-versa; pick one and align).

## Scope

**Files:**
- `Stash.Registry/appsettings.json` — fix `MaxAttempts` and `WindowSeconds` defaults.
- `Stash.Registry/appsettings.Development.json` — same.
- `Stash.Registry/README.md` and any docs under `Stash.Registry/docs/` — fix the documented section prefix.
- `Stash.Registry/Configuration/RateLimitOptions.cs` (or wherever the `IOptions<>` is bound) — confirm the actual binding prefix; if the docs are the canonical intent, change the code. Otherwise change the docs.

**Changes:**

1. **Sensible rate-limit defaults:** `MaxAttempts: 5`, `WindowSeconds: 60` (5 requests per minute is a conservative starting point — match what the docs recommend). Even with `Enabled: false`, the values should be valid so flipping the flag works without a divide-by-zero.
2. **Validation at startup:** When the rate-limit options bind, validate `MaxAttempts > 0` and `WindowSeconds > 0`. Use `IValidateOptions<RateLimitOptions>` to fail startup with a clear error if invalid values are configured. (Defense in depth — sensible defaults are not enough; a self-hoster who explicitly sets zero should still get a clear failure rather than divide-by-zero at runtime.)
3. **Align section prefix:** Pick the canonical name. Recommend matching what's already in code to minimize churn for any existing self-hosters; update README to match. If multiple docs reference the wrong prefix, fix them all (grep the README and `docs/` for the wrong prefix to be sure).
4. **Document the defaults** in the README so users know what they get when `Enabled: true` without overriding values.

## Acceptance Criteria

- [ ] `appsettings.json` ships with `MaxAttempts > 0` and `WindowSeconds > 0` even when `Enabled: false`.
- [ ] Flipping `Enabled: true` with no other config edits produces a working rate-limited registry (not divide-by-zero, not all-blocked).
- [ ] Startup with `MaxAttempts: 0` or `WindowSeconds: 0` fails with a clear validation error naming the bad field.
- [ ] The config section prefix in the README exactly matches the binding key in code.
- [ ] xUnit test boots with valid defaults and confirms rate limiter works; second test boots with explicit zero values and confirms startup validation rejects.

## Out of Scope

- Redesigning the rate-limit algorithm (sliding-window, token bucket, etc.) — separate spec.
- Per-route rate-limit overrides.
