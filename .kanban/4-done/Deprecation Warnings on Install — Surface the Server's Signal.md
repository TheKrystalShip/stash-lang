# Deprecation Warnings on Install — Surface the Server's Signal

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** High
> **Discovery context:** Stash package registry audit — findings **I7** and **F6**.

## Background

The registry already supports deprecating a package version: `stash pkg deprecate <pkg>@<ver> "reason"` sets a `deprecated` field on the version record, and the field is included in version metadata responses (the `GET /packages/{name}/versions/{version}` endpoint and the manifest returned during dependency resolution).

The CLI ignores it:

- **I7 — Install does not warn on deprecated dependencies.** When the dependency resolver picks a version whose record has a `deprecated` field set, nothing is printed. npm prints `npm warn deprecated foo@1.2.3: <reason>` for every deprecated transitive dep — this is one of the most useful signals npm provides. We have the data; we just throw it away.
- **F6 — `stash pkg info <pkg>` ignores the `deprecated` field.** The info command renders the package's metadata (description, versions, owner, etc.) but does not display the `deprecated` field on a version, even when present. A user running `info` to check whether a version is safe to use cannot find out.

## Scope

**Files (CLI side, in `Stash.Cli`):**
- `Stash.Cli/Pkg/Commands/InstallCommand.cs` (or wherever the resolver loops over chosen versions) — print a warning for each deprecated version selected.
- `Stash.Cli/Pkg/Commands/InfoCommand.cs` — render the `deprecated` field per version.
- `Stash.Cli/Pkg/RegistryClient.cs` — confirm the version metadata DTO includes the `deprecated` field; if not, add it.

**Changes:**

1. **Install-time warnings:**
   - After dependency resolution finishes and the install plan is materialized, walk the plan. For each version whose metadata has a non-null/non-empty `deprecated` field, print:
     `warning: <name>@<version> is deprecated: <reason>`
   - Format: stderr, prefixed with `warning:` (lowercase, matches existing CLI style — confirm by grepping existing warnings).
   - One line per deprecated dep. Print at most once per (name, version) even if the dep appears transitively multiple times.
   - Do not fail the install. Deprecation is a warning, not an error.
2. **`info` command display:**
   - In the per-version section of the `info` output, add a `Deprecated:` row when the field is set, showing the reason.
   - When listing versions in compact mode (e.g., a versions table), append `(deprecated)` after the version number.

## Acceptance Criteria

- [ ] Installing a package whose chosen version is deprecated prints exactly one warning line to stderr in the documented format.
- [ ] Transitive deprecated deps also produce warnings.
- [ ] Each deprecated (name, version) pair warns at most once per install.
- [ ] `stash pkg info <pkg>` renders the `deprecated` field when set.
- [ ] xUnit tests cover: install with one deprecated dep, install with multiple deprecated deps (no duplicates), install with no deprecated deps (no extra output), `info` rendering with and without deprecation set.

## Risk / Notes

- Decide whether warnings go to stderr or stdout. npm uses stderr — match. Confirm the rest of the CLI does the same so scripts that capture stdout aren't affected.
- A future spec may add a `--no-warnings` or `--silent` flag; do not pre-build that flag here.

## Out of Scope

- Failing the install on deprecation (some ecosystems offer this as opt-in; out of scope).
- Suggesting replacement versions automatically.
