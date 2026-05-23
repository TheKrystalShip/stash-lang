# Inline Pipes — Enable `$(cmd1 | cmd2)` Test Suite

**Status:** Ready for implementation
**Created:** 2026-04-26
**Author:** Architect
**Priority:** Low (no new code — tests and docs only)

---

## 1. Summary

The inline pipe syntax — `$(cmd1 | cmd2)` where the `|` lives _inside_ a single `$(...)` — is **already fully implemented** via the `PipeChain` opcode (65) introduced in the streaming pipe spec. The 9 tests covering this syntax were skipped because they predated that implementation and were left untouched to avoid touching other pipe work.

This spec covers what's left:

1. Un-skip and fix 9 tests in `InterpreterTests.cs`
2. Update `docs/Stash — Language Specification.md` to formally document the syntax
3. Create `examples/pipes.stash` showcasing both inline and external pipe styles

**No new opcodes, AST nodes, or VM changes are required.**

---

## 2. How Inline Pipes Already Work

The pipeline from source to execution is identical to external pipes:

```
$(echo hello | cat)
      ↓ Lexer (ScanCommandLiteral — splits on | at depth=1)
CommandLiteral("$(echo hello)")  Pipe  CommandLiteral("$(cat)")
      ↓ Parser (Pipe() method — left-associative PipeExpr chain)
PipeExpr(CommandExpr("echo hello"), CommandExpr("cat"))
      ↓ Compiler (VisitPipeExpr → FlattenPipeChain)
PipeChain opcode (2 stages, companion words)
      ↓ VM (ExecutePipeChain → ExecPipelineStreaming)
Concurrent OS pipes, pump threads, CommandResult
```

Key lexer fact (`Stash.Core/Lexing/Lexer.cs`, `ScanCommandLiteral`): when the lexer encounters `|` at nesting depth 1 inside a `$(...)`, it:

1. Emits the current segment as a `CommandLiteral` token (with the same prefix — `$(`, `$!(`, etc.)
2. Emits a `Pipe` token
3. Continues scanning the next segment

This means `$!(cmd1 | cmd2)` emits two `StrictCommandLiteral` tokens — both stages get `IsStrict = true` in the AST. The compiler propagates `IsStrict` into each stage's companion word flag. The VM applies strict mode **only to the last stage** (per the `PipeChain` spec §4.3), which is the correct POSIX semantics.

`$>(cmd1 | cmd2)` does NOT emit pipe tokens (the lexer checks `!passthrough` before splitting on `|`), so passthrough + inline pipe is naturally a parse error (unexpected token) rather than a compiler error. That is acceptable behavior.

---

## 3. Tests to Un-skip

All 9 tests are in `Stash.Tests/Interpreting/InterpreterTests.cs`. Each currently has:

```csharp
[Fact(Skip = "Pipes are blocking the test suite due to a deadlock issue. Need to investigate and fix before re-enabling.")]
```

### 3.1 Core Inline Pipe Tests (lines ~2489–2537)

| Test name                      | Stash code                               | Assertion                 | Platform  |
| ------------------------------ | ---------------------------------------- | ------------------------- | --------- |
| `Pipe_InlineBasicChain`        | `$(echo hello \| cat)`                   | stdout contains "hello"   | Unix only |
| `Pipe_InlineThreeStages`       | `$(printf "3\n1\n2" \| sort \| head -2)` | contains "1","2"; not "3" | Unix only |
| `Pipe_InlineMixedWithExternal` | `$(echo hello \| cat) \| $(cat)`         | stdout contains "hello"   | Unix only |
| `Pipe_InlineWithInterpolation` | `$(echo hello world \| grep ${pattern})` | stdout contains "hello"   | Unix only |
| `Pipe_InlineLargeData`         | `$(seq 1 10000 \| wc -l)`                | stdout.Trim() == "10000"  | Unix only |
| `Pipe_InlineStreamingHead`     | `$(yes \| head -5)`                      | exactly 5 lines, all "y"  | Unix only |

For each test, replace the `[Fact(Skip = "...")]` with `[Fact]` plus `[Trait("Category", "Unix")]` and add `if (OperatingSystem.IsWindows()) return;` as the first line of the test body.

`Pipe_InlineThreeStages` additionally asserts `DoesNotContain("3", stdout)` — leave this as-is.

`Pipe_InlineStreamingHead` has the split/count assertion — leave as-is.

### 3.2 Strict Command Pipeline Tests (lines ~9609–9648)

| Test name                                             | Stash code                                                            | Assertion                                                | Platform  |
| ----------------------------------------------------- | --------------------------------------------------------------------- | -------------------------------------------------------- | --------- |
| `StrictCommand_Pipeline_Success_ReturnsCommandResult` | `$!(echo hello \| tr a-z A-Z)`                                        | stdout contains "HELLO"                                  | Unix only |
| `StrictCommand_Pipeline_Failure_ThrowsCommandError`   | `$!(echo hello \| false)`                                             | throws `RuntimeError` with `ErrorType == "CommandError"` | Unix only |
| `StrictCommand_Pipeline_Failure_CaughtExitCode`       | `try { $!(echo hello \| false); } catch (e) { result = e.exitCode; }` | result is `long`, not 0                                  | Unix only |

For each, replace `[Fact(Skip = "...")]` with `[Fact]` plus `[Trait("Category", "Unix")]` and `if (OperatingSystem.IsWindows()) return;` as first line of body.

Do not change the assertion logic in any test — only the skip attribute and platform guard.

---

## 4. Language Spec Update

**File:** `docs/Stash — Language Specification.md`

In the Pipes section, there is already documentation for the external `$(cmd1) | $(cmd2)` syntax. Add a subsection (or expand the existing section) to formally document the inline variant:

### Inline Pipe Syntax

```
InlinePipe ::= "$(" PipedCommand ")"
             | "$!(" PipedCommand ")"
PipedCommand ::= CommandPart ("|" CommandPart)*
CommandPart  ::= <whitespace-trimmed command text with ${} interpolations>
```

Semantics to document:

- `$(cmd1 | cmd2)` is equivalent to `$(cmd1) | $(cmd2)` — both compile to the same `PipeChain` opcode with identical streaming semantics
- `$!(cmd1 | cmd2)` — strict mode applies to the **last stage only** (POSIX semantics); intermediate non-zero exit codes are ignored
- `$(cmd1 | cmd2) | $(cmd3)` — inline and external pipes compose freely; the result is a 3-stage `PipeChain`
- `$>(cmd1 | cmd2)` — passthrough commands do not support inline pipes (natural parse error)
- String interpolation works in any stage: `$(echo ${x} | grep ${pattern})`

Include a code example:

```stash
// Inline pipe — all stages inside one $()
let result = $(echo "hello world" | tr 'a-z' 'A-Z' | rev);
io.println(result.stdout);  // → "DLROW OLLEH"

// Combine with external pipe
let lines = $(seq 1 100 | grep "^5") | $(wc -l);
io.println(lines.stdout);   // → "11"

// Strict mode on inline pipe — throws if last stage fails
let upper = $!(echo hello | tr a-z A-Z);
```

---

## 5. Example Script

**File:** `examples/pipes.stash`

Create a comprehensive example covering both inline and external pipe variants:

```stash
// pipes.stash — Stash pipe operator examples

// ── External pipes: $(cmd1) | $(cmd2) ─────────────────────────────────────
// Each stage is an independent $() expression
let result = $(echo "hello world") | $(cat);
io.println(result.stdout);   // → "hello world"

// Exit code comes from the last stage
let r = $(false) | $(echo "ran anyway");
io.println(r.exitCode);      // → 0 (echo succeeded)

// Three stages
let sorted = $(printf "3\n1\n2") | $(sort) | $(head -2);
io.println(sorted.stdout);   // → "1\n2"

// ── Inline pipes: $(cmd1 | cmd2) ──────────────────────────────────────────
// All stages inside one $(). Identical runtime semantics.
let upper = $(echo "hello world" | tr 'a-z' 'A-Z');
io.println(upper.stdout);    // → "HELLO WORLD"

// Interpolation in any stage
let pattern = "world";
let filtered = $(echo "hello\nworld" | grep ${pattern});
io.println(filtered.stdout); // → "world"

// Inline + external compose freely
let composed = $(echo "hello" | cat) | $(cat);
io.println(composed.stdout); // → "hello"

// ── Strict mode ───────────────────────────────────────────────────────────
// $!(cmd1 | cmd2) — throws CommandError if the last stage exits non-zero
try {
    $!(echo "hello" | false);
} catch (e) {
    io.println(e.code);      // → "CommandError"
    io.println(e.exitCode);  // → 1
}

// Strict applied to external pipe — only the last $(!) stage is strict
let check = $(false) | $!(echo "ok");
io.println(check.exitCode);  // → 0 (echo ok succeeded)
```

---

## 6. Implementation Checklist

- [ ] Un-skip 6 core inline pipe tests in `InterpreterTests.cs` with `[Trait("Category", "Unix")]` + Windows guard
- [ ] Un-skip 3 strict command pipeline tests in `InterpreterTests.cs` with same guards
- [ ] Update `docs/Stash — Language Specification.md` — add inline pipe subsection
- [ ] Create `examples/pipes.stash`
- [ ] Run `dotnet test --filter "FullyQualifiedName~Pipe"` — all should pass (or skip on Windows)
- [ ] Run full test suite — zero new failures

---

## 7. Out of Scope

- `$>(cmd1 | cmd2)` — passthrough + inline pipe. Already produces a natural error; no explicit handling needed.
- Nested `$($(inner) | cat)` — already works via existing interpolation and depth tracking; no additional changes needed.
- Windows equivalents for `sort`, `tr`, `head`, `seq` — these tests are Unix-only by design.
