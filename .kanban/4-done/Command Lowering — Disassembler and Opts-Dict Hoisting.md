# Command Lowering — Disassembler Annotation & Opts-Dict Hoisting

- **Status:** Backlog / Analysis — Phase 1 (§3.1) DONE 2026-05-14; Phase 2 (§3.2) still backlogged pending benchmark
- **Created:** 2026-05-14
- **Scope:** Cosmetic + small VM-side optimisation for command syntax (`$(...)`, `$!(...)`, `$!>(...)`, pipelines)
- **Origin:** Disassembly of `build.stash` showed a long run of `load.k` instructions all annotated `StashLiteralArg`, which looked like redundant constants. Investigation confirmed the constants are not redundant; the disassembler is misleading.

## 1. Problem

Disassembling shell-exec syntax produces output like:

```
load.k r6, k3  ; StashLiteralArg
load.k r7, k4  ; StashLiteralArg
load.k r8, k5  ; StashLiteralArg
```

Each `k_n` is a distinct constant slot, but the annotation column shows only the C# type name. This makes it impossible to tell at a glance whether (a) the constant pool is failing to dedupe identical tokens, (b) the compiler is needlessly re-materialising the same value, or (c) the constants legitimately differ.

Separately, every command call site re-builds the options dict (`{ strict: true }`, `{ mode: "Passthrough", strict: true }`, etc.) from per-key `LoadK` plus `NewDict`, even when all entries are compile-time constants.

## 2. Findings

### 2.1 Constants are correctly deduped — disassembler is the issue

- `StashLiteralArg` (`Stash.Core/Runtime/Types/StashLiteralArg.cs`, lines 24-66) carries `Text` (string) and `ShouldExpand` (bool) and implements value-based `Equals` / `GetHashCode`.
- `ChunkBuilder.AddConstant` (`Stash.Bytecode/Bytecode/ChunkBuilder.cs`, line 121) keys `_constantMap` with `StashValueComparer` (line 1215), whose `Obj`-tag branch dispatches to `object.Equals` — i.e. uses the type's overridden equality (line 1228).
- Result: two `new StashLiteralArg("Release", true)` instances collapse into one slot. The seven slots in `dotnet publish -c Release -r ${RUNTIME} --self-contained ...` are not duplicates — each holds a different token (`-c`, `Release`, `-r`, ...).
- `Disassembler.FormatConstant` (`Stash.Bytecode/Bytecode/Disassembler.cs`, lines 655-672) has explicit arms for `string`, `Chunk`, `string[]`, `LockMetadata`, then falls through to `value.AsObj?.GetType().Name` — which prints "StashLiteralArg" for every such constant regardless of contents.

### 2.2 Opts dict is re-built every call

`Compiler.CommandLowering.EmitOptsDict` (lines 401-460) and `EmitSingleRedirectDict` (lines 487-524) emit a fresh `LoadK` + `LoadK` (or `LoadBool`) + ... + `NewDict` sequence at every call site. For build scripts with many `$!>(...)` calls, the dict `{ mode: "Passthrough", strict: true }` is rematerialised on every hit.

The redirect-free opts dicts are entirely compile-time constant: the set of possible payloads is:

| Form                  | Opts dict                                |
| --------------------- | ---------------------------------------- |
| `$(...)`              | none (LoadNull)                          |
| `$!(...)`             | `{ strict: true }`                       |
| `$>(...)`             | `{ mode: "Passthrough" }`                |
| `$!>(...)`            | `{ mode: "Passthrough", strict: true }`  |
| streaming variants    | `{ mode: "Stream", ... }`                |
| with redirect         | non-constant (target is an expression)   |

Only the redirect-bearing variant has a runtime-dependent field; everything else is a frozen literal dict.

## 3. Proposed Work

### 3.1 Disassembler annotation (mandatory, trivial) — DONE 2026-05-14

Implemented in `Stash.Bytecode/Bytecode/Disassembler.cs` `FormatConstant` (added `StashLiteralArg` arm + `using Stash.Runtime.Types;`). Disassembling `$!>(dotnet clean -c Release);` now emits `LiteralArg("clean")`, `LiteralArg("-c")`, `LiteralArg("Release")` in the constant pool listing and on the inline `; ` annotations next to each `load.k`, instead of the bare type name `StashLiteralArg`. No existing snapshot/disassembler tests cover this code path (`Stash.Tests/Bytecode/DisassemblerTests.cs` only tests `SourceMap`), so no test was added — pure dev-tooling change, language semantics/stdlib/grammar unaffected, so no spec/docs/example updates per `.claude/language-changes.md`.

Original sketch (for reference):

Add a `StashLiteralArg` arm in `FormatConstant`:

```csharp
StashLiteralArg la => la.ShouldExpand
    ? $"LiteralArg(\"{EscapeString(la.Text)}\")"
    : $"LiteralArg(\"{EscapeString(la.Text)}\", verbatim)",
```

- File: `Stash.Bytecode/Bytecode/Disassembler.cs`, around line 668.
- No semantic impact, no test churn beyond a disassembly snapshot test if one exists.
- Closes the "looks wrong" reporting issue at zero cost.

### 3.2 Opts-dict hoisting (optional, evaluate before scheduling)

**Design sketch:**

- At compile time, when `EmitOptsDict` would emit a payload whose every value is a primitive constant (and there are no redirects), construct a frozen `StashDictionary` and intern it via `AddConstant`. Emit a single `LoadK` in place of the multi-instruction build.
- Requires either:
  - A "frozen dict" marker on `StashDictionary` so `process.exec` (and any other consumer) treats it as read-only and either copies on read or asserts no mutation, OR
  - A guarantee from `process.exec` that it never mutates its `opts` argument (likely already true; verify in `ProcessBuiltIns.cs`).

**Open questions:**

1. Is the dict actually a measurable hot spot? Need a microbench: a loop calling `$!>(echo hi)` 100k times, compare current vs hoisted. If <2% of total time, the disassembler fix alone is enough — defer this.
2. Does any current code path mutate `opts` after `process.exec` receives it? If yes, hoisting requires defensive clone (defeating the win).
3. Should redirect dicts get a partial hoist (constant `stream`/`append` slots resolved at compile time, only `target` slot computed)? Probably overkill — the redirect path is rarer and the savings smaller.

**Risks:**

- Adding "frozen" semantics to dicts is a real language-surface change if it leaks into user code. The hoist must remain a private compiler optimisation; the constant pool entry should not be observable from user code (it never is today — opts dicts are internal to the lowering).
- Constant-pool growth: one slot per distinct opts shape. With ~6 distinct shapes total, negligible.

### 3.3 What we are **not** changing

- The argv `StashLiteralArg` constants — they are doing the right thing already and will look correct once §3.1 lands.
- The `NewArray` for argv — the array literally has to be built fresh on every call because some slots may be runtime expressions. Hoisting would require splitting into "constant prefix array + dynamic suffix splat" lowering, which is not worth the complexity.

## 4. Test Scenarios

For §3.1:
- Snapshot test: disassemble a `$!>(dotnet -c Release)` and assert the annotation column shows `LiteralArg("-c")` and `LiteralArg("Release")` rather than the bare type name.

For §3.2 (if pursued):
- Bytecode test: compile `$!>(echo hi)` and assert the opts dict slot is a single `LoadK` referencing a `StashDictionary` constant.
- Runtime test: ensure repeated calls observe the same opts dict instance (or a clone, depending on chosen semantics) and that mutating fields in a (hypothetical) `process.exec` implementation does not bleed across calls.
- Regression test for the redirect path: opts dicts with a redirect entry must still go through the full builder.

## 5. Decision Log

- **2026-05-14** — Disassembler annotation accepted as the minimum-viable fix; opts-dict hoisting parked pending a microbenchmark of command-heavy scripts. The original suspicion (constant-pool dedup failure) was disproved.
