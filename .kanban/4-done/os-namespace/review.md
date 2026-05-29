# os-namespace — Review

> Produced by `/feature-review`. One finding per H2 section.
> Each finding header MUST be parseable: `## Fxx — [SEVERITY] short title`.
> `/resolve <feature> Fxx [Fyy...]` reads the selected section(s) and dispatches a Resolver.

**Scope reviewed:** commits `30da39bf953309ff49d88b0e41acff705f9567b3..339f4220714a3e554e5c54312df81b952b8091d7` on branch `main`
**Brief:** ../brief.md
**Generated:** 2026-05-29

---

## F01 — [CRITICAL] io.pathSeparator / io.newLine missing throws metadata classification (Wave1 coverage regression)

**Status:** fixed
**Fixed in:** 0a8c5c0
**Files:** `Stash.Tests/Stdlib/SourceGenerator/Wave1ThrowsCoverageTests.cs:16`, `Stash.Stdlib/BuiltIns/IoBuiltIns.cs:138-148`
**Phase:** P5
**Commit:** 442f4a4

### Observation

`Wave1ThrowsCoverageTests` fails after P5 added `io.pathSeparator()` and `io.newLine()`:

- `Wave1_EveryFunctionHasThrowsOrIsAllowlisted(namespaceName: "io")` fails with: *"io: 2 function(s) lack throws metadata and are not in the no-throw allow-list: pathSeparator, newLine. Either add `<exception>` tags or update NoThrowAllowList in this test."*
- `Wave1_AllFunctionsTagged_CoverageCheckPasses` — `Assert.Equal()` expected `9`, actual `7` (the `io` namespace gained two functions; the allow-list still contains only the original 5 names).

The `io` allow-list at `Wave1ThrowsCoverageTests.cs:16` reads:
```
["io"] = new() { "println", "print", "eprintln", "eprint", "readLine" },
```
The two new functions are infallible (`Path.PathSeparator.ToString()` and `Environment.NewLine` access — both pure runtime constants that cannot throw), so they belong in the allow-list, mirroring how sibling no-throw `io` reads (`readLine`, `println`, `print`, …) are classified.

### Why this matters

Hard, feature-attributable test failures block `/done`. Two Wave1 throws-coverage tests are red on the developer host today, unrelated to any documented flaky bucket. The fix is mechanical but mandatory before promotion.

### Suggested fix

Extend the `io` allow-list in `Stash.Tests/Stdlib/SourceGenerator/Wave1ThrowsCoverageTests.cs` to include the two additions:

```csharp
["io"] = new() { "println", "print", "eprintln", "eprint", "readLine", "pathSeparator", "newLine" },
```

Both functions are infallible reads; no `<exception>` tags are warranted. Do not add `<exception>` XML to the implementation — the allow-list path is the correct classification for guaranteed-no-throw functions per the existing sibling pattern.

### Verify

```
dotnet test --filter "FullyQualifiedName~Wave1ThrowsCoverageTests"
```

---

## F02 — [CRITICAL] LSP completion snapshots not regenerated after os namespace addition

**Status:** fixed
**Fixed in:** 0a8c5c0
**Files:** `Stash.Tests/Lsp/Snapshots/default-with-snippets.completion.txt`, `Stash.Tests/Lsp/Snapshots/empty-file.completion.txt`
**Phase:** P1 (surfaced after P1; not re-baselined through P8)
**Commit:** 0a07c2b (root cause), final state at 339f422

### Observation

`Stash.Tests.Lsp.CompletionSurfaceSnapshotTests` fails on two tests:

- `Snapshot_Default_WithSnippets`
- the `empty-file` variant

Re-running with `STASH_SNAPSHOT_REGEN=1` produces a purely additive diff: each fixture needs exactly one new line `Module<TAB>os` inserted alphabetically between `net` and `path`. Nothing is dropped or reordered.

This is the new `os` namespace correctly surfacing in LSP completion. The committed snapshot baselines were never re-baselined when the namespace was added in P1, so the snapshots fail despite the LSP behaving exactly as expected.

### Why this matters

The `.claude/language-changes.md` checklist explicitly requires verifying LSP tooling compatibility after every language/stdlib change. The completion-surface snapshot is the canonical guard for "does the LSP advertise the right namespaces?" and it is red. `/done` cannot pass until these two embedded fixtures are re-baselined.

### Suggested fix

Re-baseline both completion fixtures using the documented procedure in `Stash.Tests/CLAUDE.md` "Completion surface snapshots":

```bash
STASH_SNAPSHOT_REGEN=1 dotnet test --filter FullyQualifiedName~CompletionSurfaceSnapshotTests
```

Then commit the diff to `Stash.Tests/Lsp/Snapshots/default-with-snippets.completion.txt` and `Stash.Tests/Lsp/Snapshots/empty-file.completion.txt`. The expected diff is a single inserted line per file (`Module\tos` between `net` and `path`); confirm no other lines move before committing.

### Verify

```
dotnet test --filter "FullyQualifiedName~CompletionSurfaceSnapshotTests"
```

---

## F03 — [IMPORTANT] final_verify uses bare `dotnet test` — violates CLAUDE.md filter requirement

**Status:** fixed
**Fixed in:** 2a8b93d
**Files:** `.kanban/2-in-progress/os-namespace/plan.yaml:156-158`
**Phase:** cross-phase (plan-level)
**Commit:** -

### Observation

`plan.yaml`'s `final_verify` block reads:

```yaml
final_verify:
  - dotnet build
  - dotnet test
```

`CLAUDE.md` is explicit: *"When authoring `plan.yaml`, narrow `final_verify`'s `dotnet test` step to exclude documented flaky / environment-dependent classes (see `.claude/repo.md` 'Known Issues'). Bare `dotnet test` fails `/done` due to pre-existing `DiffPackageTests`, `Registry*Tests`, `NetBuiltInsTests`, parallel-execution flakies, etc. — these are not feature regressions."*

The current bare invocation will fail `/done` even after F01 and F02 are resolved, because of the ~35 `DiffPackageTests` failures and ~6 `Registry*Tests` failures already observed against HEAD — neither of which the os-namespace feature touches.

The precedent filter shape lives in the `exports-private-default` and `stdlib-namespace-members` `plan.yaml` files.

### Why this matters

Without this filter, `/done` is impossible regardless of feature correctness. The CLAUDE.md rule exists precisely to keep `/done` honest about *feature regressions* vs *environmental noise*; the os-namespace plan slipped through without applying it.

### Suggested fix

Replace the `dotnet test` step in `final_verify` with a positive-namespace filter following the canonical precedent. Inspect `.kanban/4-done/exports-private-default/plan.yaml` and `.kanban/4-done/stdlib-namespace-members/plan.yaml` for the exact filter shape; adapt the include-list to cover the namespaces this feature actually exercises (at minimum: `OsBuiltInsTests`, `IoBuiltInsTests`, `EnvBuiltInsTests`, `EnvMembersTests`, `StdlibConsistencyTests`, `StandardLibraryReferenceTests`, `Wave1ThrowsCoverageTests`, `CompletionSurfaceSnapshotTests`, `InterpreterTests`).

### Verify

```
python3 scripts/checkpoint/validate-spec.py os-namespace
# then run the new final_verify command list and confirm green
```

---

## F04 — [MINOR] os.isLinuxVersionAtLeast uses Environment.OSVersion.Version, divergent from Windows/macOS pattern

**Status:** fixed
**Fixed in:** 9510ade
**Files:** `Stash.Stdlib/BuiltIns/OsBuiltIns.cs:332-339`
**Phase:** P3
**Commit:** c496c2a

### Observation

`isMacOSVersionAtLeast` and `isWindowsVersionAtLeast` delegate directly to `OperatingSystem.IsMacOSVersionAtLeast(...)` / `OperatingSystem.IsWindowsVersionAtLeast(...)`. `isLinuxVersionAtLeast` cannot follow this pattern because .NET does not expose `OperatingSystem.IsLinuxVersionAtLeast`. The implementation hand-rolls the comparison against `Environment.OSVersion.Version`:

```csharp
[StashFn]
public static bool IsLinuxVersionAtLeast(long major, long minor = 0L)
{
    if (!OperatingSystem.IsLinux()) return false;
    var v = Environment.OSVersion.Version;
    if (v.Major != (int)major) return v.Major > (int)major;
    return v.Minor >= (int)minor;
}
```

Verdict: **acceptable** — this is the only way to express "Linux kernel version at least" given the .NET surface, the wrong-host short-circuit (`return false; never throw`) matches the brief's acceptance criterion, and `Environment.OSVersion.Version` on Linux returns the kernel `uname -r` numeric components, which is the natural Stash-script use case. However, two minor concerns are worth recording:

1. The XML `<summary>` says *"the kernel version"*; the spec's user-facing contract should probably say so explicitly (today the brief just lists `isLinuxVersionAtLeast(major, minor?)` symmetrically with the Windows/macOS helpers, without flagging the semantic asymmetry).
2. The comparison drops the `Build` and `Revision` fields. On Linux, `Environment.OSVersion.Version.Build` typically maps to the kernel patch number (e.g. `6.1.0` → Build=0, but real values exist). Callers asking *"are we on 6.1.0 or newer"* with `isLinuxVersionAtLeast(6, 1)` will get `true` on `6.1.0` (good) but the API gives no way to express `>= 6.1.5`. That mirrors a deliberate choice in the brief (only `major, minor`) — acceptable as a forward-compat narrowing.

### Why this matters

Documentation drift only. The behavior is correct and the wrong-host guard works. But a Stash user reading the spec might reasonably expect `isLinuxVersionAtLeast` to mean "OS distro version" (since on Windows/macOS the helper is about the OS, not the kernel); on Linux there is no distro version available from `Environment.OSVersion`, only the kernel version. The XML `<summary>` says "kernel version", but the spec / stdlib reference will surface the helper without that nuance.

### Suggested fix

Add a one-sentence note to `docs/Stash — Language Specification.md` (and ensure it propagates into the regenerated stdlib reference) clarifying: *"On Linux, `os.isLinuxVersionAtLeast(major, minor)` compares against the kernel version (`uname -r` components), not a distribution release number, because .NET exposes only `Environment.OSVersion.Version` and no `IsLinuxVersionAtLeast` helper."*

No code change is required.

### Verify

```
grep -n "isLinuxVersionAtLeast" "docs/Stash — Language Specification.md" "docs/Stash — Standard Library Reference.md"
# Confirm the kernel-version disclaimer appears in user-facing docs.
```

---

## F05 — [MINOR] PlatformInfo construction duplicates per-field logic, inviting drift from individual helpers

**Status:** fixed
**Fixed in:** 9510ade
**Files:** `Stash.Stdlib/BuiltIns/OsBuiltIns.cs:276-293`
**Phase:** P4
**Commit:** e9ba10b

### Observation

`Info()` re-implements the field computations inline rather than calling the individual `[StashFn]` helpers:

```csharp
["arch"]        = StashValue.FromObj(RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()),
["processArch"] = StashValue.FromObj(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()),
["description"] = StashValue.FromObj(RuntimeInformation.OSDescription),
["framework"]   = StashValue.FromObj(RuntimeInformation.FrameworkDescription),
["version"]     = StashValue.FromObj(Environment.OSVersion.VersionString),
["endianness"]  = StashValue.FromObj(BitConverter.IsLittleEndian ? EndiannessLittle : EndiannessBig),
```

Each line repeats the `.NET` call that the corresponding public helper (`Arch()`, `ProcessArch()`, `Description()`, `Framework()`, `Version()`, `Endianness()`) already wraps. Tests in `OsBuiltInsTests` enforce field-equality between `info()` and the individual helpers, so any future drift will be caught — but the structural risk is real if a helper's underlying API ever changes (e.g. someone adds normalization to `Arch()` but forgets `Info`).

Acceptance criterion in the brief is met: *"`os.info()` returns a `PlatformInfo` whose fields match the individual helpers"* — currently true, by construction.

### Why this matters

Pure maintainability. The brief explicitly states "fields mirror the individual `os.*` helpers"; routing through the helpers (where C# allows it) would make that mirror structural rather than test-enforced. Some helpers are public `[StashFn]` methods that return primitive C# types — those can be called directly from `Info()` without round-tripping through Stash. Others (e.g. `GetPlatform`, which is `Raw = true` and returns `StashValue`) can't easily be reused, but the underlying `DetectPlatform()` already factors out the platform detection — apply the same pattern to the remaining fields.

### Suggested fix

Refactor `Info()` to call the public helpers where possible:

```csharp
var platform = DetectPlatform();
var fields = new Dictionary<string, StashValue>(9)
{
    ["platform"]    = StashValue.FromObj(new StashEnumValue(nameof(Platform), platform.ToString())),
    ["name"]        = StashValue.FromObj(platform.ToString().ToLowerInvariant()),
    ["isUnix"]      = StashValue.FromBool(IsUnix()),
    ["arch"]        = StashValue.FromObj(Arch()),
    ["processArch"] = StashValue.FromObj(ProcessArch()),
    ["description"] = StashValue.FromObj(Description()),
    ["framework"]   = StashValue.FromObj(Framework()),
    ["version"]     = StashValue.FromObj(Version()),
    ["endianness"]  = StashValue.FromObj(Endianness()),
};
```

This preserves the non-memoization guarantee (each call still evaluates fresh) and the structural mirroring becomes a compile-time fact rather than a test invariant.

### Verify

```
dotnet test --filter "FullyQualifiedName~OsBuiltInsTests"
```

All existing info-equality tests must remain green.
