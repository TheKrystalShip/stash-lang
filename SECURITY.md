# Security Policy

Thank you for helping keep Stash and its users safe.

## Supported Versions

Stash is in pre-1.0 development. While in the `0.x` series, the latest released version is the only one that receives security fixes.

| Version | Supported          |
| ------- | ------------------ |
| `0.x`   | :white_check_mark: |

Once `1.0` ships, this table will be updated with a formal support window.

## Reporting a Vulnerability

**Please do not file public GitHub issues for security problems.**

Use GitHub's private vulnerability reporting:

1. Open <https://github.com/TheKrystalShip/stash-lang/security/advisories/new>
2. Fill out the form with as much detail as you can provide:
   - Affected component (e.g., `Stash.Bytecode`, `Stash.Registry`, embedding API)
   - Affected version (`stash --version`)
   - Reproduction steps or a minimal repro script
   - Impact assessment (what an attacker could achieve)

If you are unable to use GitHub Security Advisories, email the maintainer at **cristian.moraru@live.com** with `[stash-security]` in the subject line.

## Scope

The following are in scope for security reports:

- **Interpreter sandbox escapes** — code running under `StashCapabilities.None` (or any restricted capability set) gaining access to disabled subsystems (filesystem, network, process, environment).
- **Registry authentication / authorization bypass** — `Stash.Registry` allowing unauthenticated or improperly-scoped access to publish, yank, or modify packages.
- **Package tarball traversal** — malformed `.tgz` packages writing files outside the intended extraction directory (zip-slip / path traversal) when installed via `stash pkg install`.
- **Memory-safety bugs reachable from untrusted Stash source** — VM bugs that read/write outside the value stack, infinite loops not honoring `timeout`, or compiler crashes that produce executable but corrupt bytecode.
- **Secret leakage through the `secret` type** — values wrapped in `secret(...)` appearing in plaintext through any normal language path (println, interpolation, error messages, REPL output).

The following are **not** in scope:

- Issues that require an attacker to already have arbitrary code execution on the host running Stash with full capabilities — that is the same trust boundary as any scripting interpreter.
- Crashes in the LSP / DAP servers triggered by malformed input from a trusted editor — these are bugs, not vulnerabilities.
- Denial-of-service via deeply nested expressions, very large literals, or extremely long-running scripts run with full capabilities.

## Response Expectations

Stash is currently a one-person project. The maintainer aims to:

- Acknowledge new reports within **5 business days**.
- Provide an initial assessment (in scope / out of scope, severity) within **14 days**.
- Ship a fix or coordinated disclosure timeline once severity is agreed.

These are best-effort targets — please be patient. If you have not heard back after two weeks, feel free to send a polite follow-up.

## Disclosure

We prefer **coordinated disclosure**. Once a fix is available we will:

1. Publish a release containing the fix.
2. Publish a GitHub Security Advisory with credit to the reporter (unless anonymity is requested).
3. Mention the fix in `CHANGELOG.md` with a link to the advisory.

Thanks again for taking the time to make Stash safer.
