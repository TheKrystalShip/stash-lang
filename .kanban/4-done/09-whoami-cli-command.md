# `stash pkg whoami` — Wire Up the Existing Endpoint and Client Method

> **Status:** Backlog
> **Created:** 2026-05-05
> **Priority:** Medium
> **Discovery context:** Stash package registry audit — finding **A2**.

## Background

A `whoami`-style command is table stakes for any CLI that uses tokens — it lets the user verify "am I logged in, and as whom?" without inspecting credential files by hand. Stash has all the parts:

- Server endpoint **`GET /auth/whoami`** exists and returns the authenticated user's record (username, email, role).
- Client method **`RegistryClient.Whoami()`** exists, calls the endpoint, and returns the deserialized response.
- **No CLI subcommand wires the two together.** The plumbing is sitting there unused.

This is a tiny command. It belongs in v1.0.

## Scope

**Files:**
- `Stash.Cli/Pkg/Commands/` — new `WhoamiCommand.cs`.
- `Stash.Cli/Pkg/PkgCommandRouter.cs` (or wherever subcommands are registered) — register `whoami`.
- `docs/PKG — Package Manager CLI.md` — add the command to the reference.

**Changes:**

1. **Command behavior:**
   - `stash pkg whoami` calls `RegistryClient.Whoami()` against the configured registry.
   - On success, prints `<username>` to stdout (one line, no decoration — matches npm's `npm whoami` style which is scriptable).
   - With `--verbose` or `-v` (if the CLI uses one of these conventions — match existing pattern), also print email, role, and the registry URL.
   - On 401 (no token / expired): print `Not logged in. Run 'stash pkg login <url>'.` to stderr and exit 1.
   - On registry not configured: same error path as the unified resolver from spec 01.
   - On network error: print error, exit 1.
2. **Help text:** `Show the username for the configured registry.` — concise, matches the CLI's existing help style.

## Acceptance Criteria

- [ ] `stash pkg whoami` prints the username on stdout when logged in.
- [ ] Exit code 0 on success, 1 on auth failure, 1 on network failure.
- [ ] Verbose flag prints additional fields.
- [ ] Help text appears in `stash pkg --help` and `stash pkg whoami --help`.
- [ ] Documentation in `docs/PKG — Package Manager CLI.md` lists the command.
- [ ] xUnit test covers the success path with a mocked client.

## Out of Scope

- Multi-registry whoami (`whoami` against a non-default registry) — add later if needed.
- Token introspection (showing token expiry, scopes) — separate command.
