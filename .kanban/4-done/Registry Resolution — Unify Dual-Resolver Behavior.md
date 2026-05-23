# Registry Resolution — Unify Dual-Resolver Behavior

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** Critical
> **Discovery context:** Stash package registry audit — finding **B2** (registry URL resolution inconsistency).

## Background

Stash package CLI commands resolve the registry URL through two different code paths that disagree on the failure mode when no registry is configured:

- **Install / search / login** → call the strict resolver, which **throws** `RegistryNotConfiguredException` (or equivalent) if no registry is configured. The user gets a clear error message telling them to run `stash pkg login` or set the registry URL.
- **Publish / unpublish / owner / deprecate / token** → call a fallback resolver that **silently substitutes a hardcoded default URL** (e.g., `https://registry.stash-lang.org` or `http://localhost:5000`) when none is configured.

This causes silent, surprising behavior: `stash pkg publish` without configured credentials will quietly hit the wrong registry, fail with an opaque 401/404, or in the worst case publish to the wrong host. Meanwhile `stash pkg install` on the same machine produces a clean error.

The two paths must behave identically. The strict behavior is correct (fail loudly, point to remediation); the fallback path is the bug.

## Scope

**Files (CLI side, in `Stash.Cli` package commands):**
- `Stash.Cli/Pkg/Commands/PublishCommand.cs`
- `Stash.Cli/Pkg/Commands/UnpublishCommand.cs`
- `Stash.Cli/Pkg/Commands/OwnerCommand.cs`
- `Stash.Cli/Pkg/Commands/DeprecateCommand.cs`
- `Stash.Cli/Pkg/Commands/TokenCommand.cs`
- `Stash.Cli/Pkg/RegistryResolver.cs` (or wherever the two resolver methods live — audit found a "strict" and a "fallback" method side-by-side)

**Change:**
1. Identify the two resolver methods. Keep only the strict one.
2. Replace every call site in publish/unpublish/owner/deprecate/token to use the strict resolver.
3. Delete the fallback resolver method and the hardcoded default URL constant.
4. Ensure the error message is identical regardless of which command triggered it (suggest: `No registry configured. Run 'stash pkg login <url>' or set the 'registry' field in stash.json.`).

## Acceptance Criteria

- [ ] All `stash pkg` subcommands that need a registry URL resolve it through a single code path.
- [ ] Running any of `publish`, `unpublish`, `owner`, `deprecate`, `token` without a configured registry produces the same user-facing error as `install` / `search` / `login` does today.
- [ ] No hardcoded fallback registry URL remains in the codebase (grep for the URL string yields zero hits in `Stash.Cli/`).
- [ ] xUnit tests in `Stash.Tests` cover: each affected command throws/exits-non-zero with the expected error message when no registry is configured.

## Out of Scope

- Changing how registries are configured (the `stash.json` `registry` field, login token storage, env var override) — only the resolution and error path is in scope here.
