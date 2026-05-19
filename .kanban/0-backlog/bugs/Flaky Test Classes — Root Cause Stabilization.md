# Flaky Test Classes — Root Cause Stabilization

**Status:** Backlog — Bug / Quality
**Created:** 2026-05-19
**Discovery context:** Surfaced during `exports-private-default` phase 1D test sweep; phase verify and `final_verify` both use a positive namespace filter to exclude these classes.

---

## Problem

Several test classes in `Stash.Tests/` fail non-deterministically or fail on developer machines and CI environments where the required external resources (network, file-watcher, shell) are unavailable or slow. An unfiltered `dotnet test` therefore cannot be treated as a reliable gate.

The affected classes and their failure modes are:

| Test class | Symptom | Root cause |
|---|---|---|
| `NetBuiltInsTests` | DNS resolution failures | Tests call actual DNS resolution; fail when network is unavailable or DNS is slow |
| `FsWatchBuiltInsTests` | Race conditions | File-watcher event delivery timing is not deterministic; tests use fixed sleeps rather than event synchronization |
| `SafeShellInterpolationE2ETests` | Environment sensitivity | Shell E2E tests depend on PATH, shell binary availability, and specific shell behavior |
| `PackageInstallerTests` | External network dependency | Tests invoke the package resolver against external registries |
| `DiffPackageTests` | Unrelated to most features | The `@stash/diff` package interpreter tests are slow and unrelated to language/VM feature work; they were explicitly marked Non-Goal in the `exports-private-default` brief |

## Impact

Every feature that narrows its `verify:` or `final_verify` filter to work around these classes is adding technical debt. The filter pattern must be repeated in each new feature's `plan.yaml`, and any new test namespace added for a feature must be explicitly included in the filter expression.

## Desired Outcome

Each affected class should be stabilized so that `dotnet test` (unfiltered) is green on any developer machine with a working build environment:

- `NetBuiltInsTests` — mock the DNS resolver or gate the tests with `[Trait("Category", "RequiresNetwork")]` + a CI environment variable skip
- `FsWatchBuiltInsTests` — replace fixed sleeps with `ManualResetEventSlim` or `SemaphoreSlim`-based synchronization; add a reasonable timeout and `Skip` if the watcher fires late
- `SafeShellInterpolationE2ETests` — gate on `[Trait("Category", "RequiresShell")]` and detect shell availability at test startup via `RuntimeInformation.IsOSPlatform` + `which`/`where` check
- `PackageInstallerTests` — introduce a mock `IPackageRegistry` and have tests use it by default; network-hitting tests become opt-in via `[Trait("Category", "RequiresNetwork")]`
- `DiffPackageTests` — no stabilization needed; consider moving to a separate `[Trait("Category", "Slow")]` collection so they can be excluded from the fast feedback loop without a namespace filter

## Out of Scope

This spec is purely about test infrastructure. No language semantics, stdlib API, or bytecode format changes are involved.
