# RFC: OS Namespace Standard Library

> **Status:** Approved (design from `.kanban/0-backlog/stdlib/OS Namespace Standard Library.md`)
> **Owner:** Cristian Moraru
> **Created:** 2026-05-22
> **Slug:** os-namespace

## Summary

Introduce a new ungated `os` standard-library namespace dedicated to platform and operating-system introspection. `os` exposes .NET's `OperatingSystem`, `RuntimeInformation`, `Environment.OSVersion`, and `BitConverter` APIs through a small, deterministic, side-effect-free surface: a `Platform` enum, per-platform predicates, raw architecture/version/endianness strings, version-at-least helpers, and an `os.info()` snapshot returning a typed `PlatformInfo` struct.

In the same change, the platform-identity helpers currently living on `env` (`env.os()`, `env.arch()`) are deleted with no compatibility aliases (pre-1.0 cleanup), all in-tree callers are migrated to `os.name()` / `os.arch()`, and the per-stream/text conventions `Path.PathSeparator` and `Environment.NewLine` are exposed as `io.pathSeparator()` and `io.newLine()` — not in `os`, since they belong with text I/O.

## Motivation

Stash must run portably across every platform .NET supports without inheriting POSIX shell semantics. Today, platform identity is scattered: `env.os()` and `env.arch()` overload the environment-variable namespace with runtime facts, there is no enum for typed branching, no way to ask for the OS version, no endianness probe, no `info()` snapshot, and no clear home for `pathSeparator` / `newLine`. Users writing portable scripts either branch on stringly-typed `env.os()` or shell out to `uname`.

A dedicated `os` namespace, backed directly by .NET's platform-detection APIs, gives users a single deterministic place to ask "what am I running on?" while keeping `env` focused on environment variables and user/session data.

## Goals

- Ship an ungated `os` namespace available in every sandbox profile.
- Use .NET runtime APIs as the source of truth — no `/etc/os-release`, no `uname` shell-outs, no registry probes.
- Expose a typed `Platform` enum covering every .NET-supported platform, plus per-platform `isXxx()` predicates and a `name()` accessor for stable lowercase strings.
- Expose raw lowercased architecture strings via `os.arch()` and `os.processArch()` — no `Architecture` enum, deliberate forward-compat tradeoff.
- Expose runtime metadata: `description()`, `framework()`, `version()`, `endianness()`.
- Expose `OperatingSystem.IsXxxVersionAtLeast(...)` helpers that return `false` (never throw) on the wrong host.
- Expose a non-memoized `os.info()` snapshot returning a `PlatformInfo` struct.
- Move `Path.PathSeparator` and `Environment.NewLine` into `io` as `io.pathSeparator()` and `io.newLine()`.
- Delete `env.os()` and `env.arch()` with no compatibility aliases and migrate all in-tree callers.
- Keep Stash portable, not POSIX-compliant.

## Non-Goals

- Do not make Stash syntax or command execution POSIX shell-compatible.
- Do not emulate POSIX utilities or shell built-ins in this namespace.
- Do not expose environment variable mutation through `os` (stays on `env`).
- Do not expose hardware/resource metrics through `os` (stays on `sys`).
- Do not expose host identity such as hostname/user through `os` (stays on `env`).
- Do not expose `newLine`/`pathSeparator` through `os` (lives on `io`).
- Do not introduce an `Architecture` enum.
- Do not memoize `os.info()`.
- Do not add a capability gate — `os` reports immutable process-scoped facts.
- Do not preserve `env.os()` / `env.arch()` as compatibility shims.

## Design

The design is a direct translation of the abstract at `.kanban/0-backlog/stdlib/OS Namespace Standard Library.md`. The full table of functions, fields, .NET sources, and rationale paragraphs from the abstract is the authoritative reference; this brief restates the structural decisions implementers must preserve.

Key locked decisions:

1. **`os.arch()` / `os.processArch()` return raw lowercased strings**, not an enum. Avoids forcing Stash to backport every new .NET architecture. `<summary>` enumerates the known-at-time-of-writing set (`x86`, `x64`, `arm`, `arm64`, `wasm`, `s390x`, `loongarch64`, `armv6`, `ppc64le`, `riscv64`) and documents that the set is open and runtime-derived.
2. **`Platform` enum covers every .NET-supported platform**: `Windows, Linux, MacOS, FreeBSD, Android, IOS, TvOS, WatchOS, Browser, Wasi, Unknown`. Member spelling follows .NET enum naming verbatim so C# stays readable next to `OperatingSystem.IsXxx()`. Only `Windows`/`Linux`/`Wasm` are tested; the rest are forward-compat.
3. **`io.pathSeparator()` and `io.newLine()` live on `io`**, not `os`, and are not duplicated in `PlatformInfo`.
4. **`env.os()` and `env.arch()` are deleted** with no compat aliases. All in-tree callers must migrate.
5. **`os.info()` is non-memoized** — each call evaluates each field afresh.
6. **No capability gate.** `os` sits in the same trust tier as `math`, `str`, `conv`.
7. **Implementation follows the existing `[StashEnum]` + `[StashStruct]` + `Raw = true` + `StashEnumValue` pattern** from `FsBuiltIns.cs` (`WatchEventType`, `FileInfo`) and `ProcessBuiltIns.cs` (`ExecMode`, `ProcessResult`).
8. **No magic strings.** All Stash-visible type names and member spellings use `nameof()` and named `private const` strings (per project rule).

### Surface

Refer to the abstract's "Public API" tables for the full, authoritative signature list. Summary:

- **Platform identity** (`os`): `platform() -> Platform`, `name() -> string`, `isWindows()/isLinux()/isMacOS()/isFreeBSD()/isAndroid()/isIOS()/isBrowser()/isUnix() -> bool`.
- **Runtime architecture and metadata** (`os`): `arch() -> string`, `processArch() -> string`, `description() -> string`, `framework() -> string`, `version() -> string`, `endianness() -> string`, `isMacOSVersionAtLeast(major, minor?, build?) -> bool`, `isWindowsVersionAtLeast(major, minor?, build?, revision?) -> bool`, `isLinuxVersionAtLeast(major, minor?) -> bool`.
- **Platform snapshot** (`os`): `info() -> PlatformInfo` with fields `platform, name, isUnix, arch, processArch, description, framework, version, endianness`.
- **Text I/O conventions** (`io`): `pathSeparator() -> string`, `newLine() -> string`.

`os.name()` strings are part of the API contract: `"windows"`, `"linux"`, `"macos"`, `"freebsd"`, `"android"`, `"ios"`, `"tvos"`, `"watchos"`, `"browser"`, `"wasi"`, `"unknown"`. `os.endianness()` returns exactly `"little"` or `"big"`.

### Semantics

- All `os` functions are pure observations of runtime/platform state. No env var reads/writes, no shelling out, no parsing of `/etc/os-release`/`uname`/registry.
- Unknown platforms return `Platform.Unknown` and `"unknown"` — never throw.
- Architecture strings are runtime-derived and normalized only via `ToLowerInvariant()`.
- `os.isUnix()` is a Stash portability convenience for `{Linux, MacOS, FreeBSD, Android, IOS, TvOS, WatchOS}` — **not** a POSIX compliance claim; the C# `<summary>` must say so.
- Version-at-least helpers return `false` on the wrong host and never throw.
- `os.info()` evaluates fields once per call; no memoization, no reference-equality guarantee across calls.

### Implementation Path

Generator metadata (`[StashEnum]` / `[StashStruct]` / `[StashFn]` on `OsBuiltIns`) drives the stdlib registry -> `OsBuiltIns` is registered in the namespace factory -> runtime can construct `StashEnumValue` for `Platform` and `StashStructInstance` for `PlatformInfo` -> tests assert metadata, identity, architecture, version, endianness, info-snapshot semantics, and the `io.pathSeparator`/`io.newLine` additions -> `env.os()` and `env.arch()` are removed and every in-tree caller (`examples/`, `Stash.Tests/`, `.github/instructions/`) is migrated -> `Stash.Docs` is regenerated to refresh the standard-library reference -> language spec / authored docs are updated to point at `os` and CHANGELOG records the breaking change.

## Acceptance Criteria

- `os` appears in `StdlibRegistry.NamespaceNames`.
- `Platform` enum metadata exposes every member listed in the abstract.
- `PlatformInfo` struct metadata exposes every field listed in the abstract.
- `os.platform()` returns the enum member matching the test host; `os.name()` returns the matching documented lowercase string.
- Exactly one of `os.isWindows()` / `os.isLinux()` / `os.isMacOS()` is true on hosts where the test host is one of those (precondition-guarded assertion).
- `os.isUnix()` matches "host is one of the documented Unix-like members" on hosts where this is deterministic.
- `os.arch()` and `os.processArch()` return non-empty lowercase strings that round-trip through `RuntimeInformation`.
- `os.description()`, `os.framework()`, `os.version()` return non-empty strings.
- `os.endianness()` is `"little"` or `"big"` and matches `BitConverter.IsLittleEndian`.
- `os.isMacOSVersionAtLeast`, `os.isWindowsVersionAtLeast`, `os.isLinuxVersionAtLeast` return `false` on the wrong host without throwing.
- `os.info()` returns a `PlatformInfo` whose fields match the individual helpers, and two successive calls produce equal values.
- `io.pathSeparator()` matches `Path.PathSeparator.ToString()`; `io.newLine()` matches `Environment.NewLine`.
- `env.os()` and `env.arch()` are absent from stdlib metadata; calling them from Stash raises an unknown-function error.
- All previously-identified in-tree callers (`examples/system_info.stash`, `examples/namespaces.stash`, both relevant `Stash.Tests` files, `.github/instructions/stdlib.instructions.md`) compile/run against the new API.
- `dotnet test` passes (full suite) and `dotnet run --project Stash.Docs/` produces a reference whose `StandardLibraryReferenceTests` are green.
- `CHANGELOG.md` documents the removal of `env.os()` / `env.arch()` and the addition of `os` and the two `io` helpers.

## Phases

See `plan.yaml`. Phases land the namespace skeleton + registration first (unblocking metadata-driven tests), then platform identity functions, then runtime architecture / version / endianness, then the `os.info()` snapshot, then `io.pathSeparator` / `io.newLine`, then env cleanup + caller migration, then the dedicated test suite, then doc regeneration + spec/CHANGELOG updates.

## Open Questions

None remaining. The eight locked design decisions were confirmed before this brief was written. Implementer-side seams (exact const naming, exact registration call site, exact placement of `PlatformInfo` construction) are left to phase work.

## Decision Log

| Date | Decision | Rationale |
| --- | --- | --- |
| 2026-05-22 | `os.arch()` / `os.processArch()` return raw lowercased strings, no `Architecture` enum. | Avoid forcing Stash to backport every new .NET architecture; documentation `<summary>` enumerates the known set and notes the set is open. |
| 2026-05-22 | `Platform` enum covers every .NET-supported platform (`Windows..Unknown`); only Windows/Linux/Wasm officially tested. | Forward compatibility; Stash runs anywhere .NET runs. |
| 2026-05-22 | `io.pathSeparator()` and `io.newLine()` live on `io`, not `os`, and are not duplicated in `PlatformInfo`. | They are per-stream/text conventions, not platform-identity facts. `io` already owns text I/O. |
| 2026-05-22 | Delete `env.os()` / `env.arch()` with no compat aliases. | Pre-1.0 cleanup; one canonical home for platform identity. |
| 2026-05-22 | `os.info()` is non-memoized. | Spec promises "evaluated once per call"; no reference-equality guarantee. Avoids surprising stale-snapshot behavior. |
| 2026-05-22 | No capability gate on `os`. | Reports immutable process-scoped facts; safe in the strictest sandbox. Same trust tier as `math`/`str`/`conv`. |
| 2026-05-22 | Follow `[StashEnum]` + `[StashStruct]` + `Raw = true` + `StashEnumValue` pattern from `FsBuiltIns.cs` and `ProcessBuiltIns.cs`. | Established convention; avoids ad-hoc registration. |
| 2026-05-22 | Use `nameof()` + named `private const` strings for all Stash-visible names. | Project-wide no-magic-strings rule. |
