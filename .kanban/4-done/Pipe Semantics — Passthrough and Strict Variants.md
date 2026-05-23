# Pipe Semantics — Passthrough and Strict Command Variants

**Status:** Ready for implementation  
**Created:** 2026-04-26  
**Author:** Architect  
**Category:** Static analysis + documentation  

---

## 1. Background & Investigation Summary

Stash has four command expression variants, distinguished by prefix:

| Syntax | Token type | `IsPassthrough` | `IsStrict` |
|--------|-----------|----------------|-----------|
| `$(cmd)` | `CommandLiteral` | false | false |
| `$!(cmd)` | `StrictCommandLiteral` | false | true |
| `$>(cmd)` | `PassthroughCommandLiteral` | true | false |
| `$!>(cmd)` | `StrictPassthroughCommandLiteral` | true | true |

The streaming pipe implementation (`PipeChain` opcode 65) was built for `$(cmd)` stages. This spec defines and documents the precise behaviour of all four variants in pipe chains, and adds a static analysis diagnostic for the one combination that is fundamentally impossible.

---

## 2. Strict (`$!`) in Pipe Chains — Already Works, Document Only

### 2.1 How it currently works

The compiler (`VisitPipeExpr` / `FlattenPipeChain`) encodes `IsStrict` as bit 0 of each stage's companion word. The VM (`ExecutePipeChain`) reads all companion words but applies the strict check **only to the last stage's exit code**. This is correct POSIX `pipefail`-without-pipefail semantics: the pipeline's outcome is determined by the last command.

This means:

| Expression | Behaviour |
|-----------|-----------|
| `$!(cmd1) \| $(cmd2)` | `cmd2` determines success/failure. `cmd1`'s strict flag is encoded but ignored at runtime. |
| `$(cmd1) \| $!(cmd2)` | `cmd2` is strict — throws `CommandError` if it exits non-zero. |
| `$!(cmd1) \| $!(cmd2)` | Same as the previous row — only `cmd2`'s strict flag is checked. |
| `$!(cmd1 \| cmd2)` (inline) | Lexer splits into two `StrictCommandLiteral` tokens. Compiler encodes both as strict. VM checks only `cmd2`'s strict flag. This is the idiomatic way to write a strict inline pipe chain. |

### 2.2 Why intermediate strict flags are ignored

The lexer's inline pipe splitting preserves the outer command's token type for **all** resulting segments. When a user writes `$!(cmd1 | cmd2)`, the lexer emits two `StrictCommandLiteral` tokens. If we emitted an SA warning for "intermediate stage has `IsStrict` set but is not the last stage", it would fire on every valid use of `$!(cmd1 | cmd2)`. This is an unacceptable false positive rate.

**Decision:** No SA rule for intermediate strict stages. Document the semantics clearly instead.

> **Note for reviewers:** If a future spec adds per-stage strict flag awareness (i.e., `$!(cmd1) | $(cmd2)` semantically means "throw if cmd1 fails" and `$(cmd1) | $!(cmd2)` means "throw if cmd2 fails"), the companion word bits are already reserved and the VM can be extended to check them per-stage. This is not in scope here.

### 2.3 Tests already passing

The following tests were un-skipped in a prior implementation cycle and all pass:

- `StrictCommand_Pipeline_Success_ReturnsCommandResult` — `$!(echo hello | tr a-z A-Z)` succeeds
- `StrictCommand_Pipeline_Failure_ThrowsCommandError` — `$!(echo hello | false)` throws `CommandError`
- `StrictCommand_Pipeline_Failure_CaughtExitCode` — exit code accessible via `e.exitCode` in catch
- `Pipe_StrictLastStage_NonZeroExitThrows` — `$(echo hello) | $!(false)` throws
- `Pipe_StrictLastStage_ZeroExitSucceeds` — `$(echo hello) | $!(cat)` succeeds

No implementation changes are needed for `$!()` variants.

---

## 3. Passthrough (`$>` and `$!>`) in Pipe Chains — Forbidden, SA Diagnostic Required

### 3.1 Why passthrough cannot participate in a pipe chain

Passthrough commands (`$>(cmd)` and `$!>(cmd)`) execute with **inherited I/O** — the process's `stdin`, `stdout`, and `stderr` are connected directly to the terminal (or whatever the parent process's standard streams are). Piping requires capturing the upstream process's `stdout` and routing it to the downstream process's `stdin` as a byte stream. These two requirements are mutually exclusive:

- To pipe, the VM must set `RedirectStandardOutput = true` and `RedirectStandardInput = true` on the `ProcessStartInfo`.
- To passthrough, the VM must leave `RedirectStandardOutput = false`, `RedirectStandardInput = false`, and `RedirectStandardError = false`.

There is no way to simultaneously inherit terminal I/O and have the VM intercept the stream for piping. This is not a Stash limitation — it is a fundamental property of POSIX process I/O model (and the equivalent on Windows).

**For inline pipes**: The lexer already handles this correctly. The inline pipe split guard is `!passthrough`:

```csharp
else if (c == '|' && depth == 1 && !passthrough)
```

So `$>(cmd1 | cmd2)` and `$!>(cmd1 | cmd2)` are **never split by the lexer**. The entire string `cmd1 | cmd2` is treated as the single command argument (which the OS shell would have to interpret if the command is `sh`, `bash`, etc.). The `|` inside a passthrough is passed verbatim to the subprocess.

**For external pipe chains**: `$>(cmd1) | $(cmd2)` is currently caught at **compile time** by `FlattenPipeChain` and `VisitPipeExpr`, which throw a `CompileError`. However, this only produces an error when the user actually compiles the code — they get no editor-time feedback.

### 3.2 What to add: SA0710

Add a static analysis diagnostic that fires during semantic validation — before compilation — so IDEs and `stash check` can surface the error immediately.

**Descriptor:**
```csharp
public static readonly DiagnosticDescriptor SA0710 = new(
    "SA0710",
    "Passthrough command in pipe chain",
    DiagnosticLevel.Error,
    "Commands",
    "A passthrough command ($>(...) or $!>(...)) cannot appear in a pipe chain. " +
    "Piping requires capturing stdout, which is incompatible with passthrough I/O inheritance. " +
    "Use $(cmd) or $!(cmd) in pipe chains.");
```

Level: **Error** (not Warning). This always prevents correct execution.

**Emission site:** `SemanticValidator.VisitPipeExpr`

### 3.3 SemanticValidator changes

The current `VisitPipeExpr` method traverses the tree via `Accept`:

```csharp
public object? VisitPipeExpr(PipeExpr expr)
{
    expr.Left.Accept(this);
    expr.Right.Accept(this);
    return null;
}
```

Replace with an implementation that:
1. Flattens the left-associative `PipeExpr` chain into a flat stage list (same algorithm as `FlattenPipeChain` in the compiler, without the exception)
2. Emits SA0710 for each stage that has `IsPassthrough = true`
3. Calls `Accept(this)` on each stage to trigger further semantic analysis (e.g., command part validation)

**Important:** Do NOT recurse through `Accept` on intermediate `PipeExpr` nodes — flatten manually to avoid double-visiting. Call `Accept` only on the leaf `Expr` nodes (the stages themselves).

**Pseudocode:**
```csharp
public object? VisitPipeExpr(PipeExpr expr)
{
    // Flatten left-associative PipeExpr chain into ordered stage list
    var stages = new List<Expr>();
    Expr current = expr;
    while (current is PipeExpr pipe)
    {
        stages.Insert(0, pipe.Right);
        current = pipe.Left;
    }
    stages.Insert(0, current); // leftmost non-pipe node

    // Validate and visit each stage
    foreach (var stage in stages)
    {
        if (stage is CommandExpr { IsPassthrough: true } cmd)
            _diagnostics.Add(DiagnosticDescriptors.SA0710.CreateDiagnostic(cmd.Span));
        
        stage.Accept(this); // visit command parts, interpolations, etc.
    }

    return null;
}
```

### 3.4 Compiler error message consistency

The compiler currently has two slightly inconsistent error messages for passthrough in pipes:
- `FlattenPipeChain`: `"Passthrough commands ($>(...)) cannot be used in a pipe chain."`
- `VisitPipeExpr` (redundant check): `"Passthrough commands ($>()) cannot appear in a pipe chain."`

Harmonize both to: `"Passthrough command ($>(...) or $!>(...)) cannot appear in a pipe chain. Use $(cmd) or $!(cmd) instead."`

The compiler check in `VisitPipeExpr` (the second check, after `FlattenPipeChain`) is redundant since `FlattenPipeChain` already throws. Remove the duplicate check in `VisitPipeExpr`'s stage loop.

---

## 4. Language Specification Updates

**File:** `docs/Stash — Language Specification.md`

Add or update content in the Pipes section to cover all four variants:

### What to document

**For `$!()` in pipe chains:**

```
#### Strict Mode in Pipe Chains

The strict modifier ($!) determines whether the pipe chain throws a `CommandError`
on non-zero exit. Only the **last stage's exit code** is checked — intermediate
exit codes are ignored regardless of whether intermediate stages use `$!()`.

  // Last stage determines outcome
  let r = $(false) | $!(echo "ran");   // succeeds — echo exits 0
  let r = $(echo "hi") | $!(false);    // throws CommandError — false exits 1

  // Inline: all stages get $! from the lexer, but only the last matters
  let r = $!(cmd1 | cmd2 | cmd3);     // throws only if cmd3 exits non-zero
  
  // External chain with explicit last-stage strict
  let r = $(cmd1) | $(cmd2) | $!(cmd3);  // equivalent semantics
```

**For `$>()` and `$!>()` in pipe chains:**

```
#### Passthrough Commands Cannot Be Piped

Passthrough commands (`$>()` and `$!>()`) run with inherited terminal I/O.
Because piping requires the VM to intercept the process's stdout stream, and
passthrough explicitly bypasses that interception, passthrough commands cannot
appear as any stage in a pipe chain. This is a compile-time error (SA0710).

  // VALID — standalone passthrough
  $>(vim file.txt);          // interactive, inherits terminal I/O

  // INVALID — compile-time error SA0710
  $(echo hello) | $>(cat);  // SA0710: passthrough cannot be piped
  $>(cmd1) | $(cmd2);        // SA0710: passthrough cannot be piped

  // VALID alternative if you want strict + piped
  $(echo hello) | $!(cat);  // strict captured pipe — use $! not $>
```

---

## 5. Implementation Checklist

- [ ] **DiagnosticDescriptors.cs** — add `SA0710` after `SA0709` with `DiagnosticLevel.Error`
- [ ] **DiagnosticDescriptors.cs** — register `SA0710` in `BuildCodeLookup()`
- [ ] **SemanticValidator.cs** — rewrite `VisitPipeExpr` to flatten chain and emit SA0710 for passthrough stages
- [ ] **Compiler.Strings.cs** — harmonize CompileError messages for passthrough in pipes; remove the duplicate check in `VisitPipeExpr`'s stage loop (keep the one in `FlattenPipeChain`)
- [ ] **Tests** — add SA analysis tests for SA0710 (at least 3 cases):
  - `$>(cmd)` on right side of external pipe
  - `$>(cmd)` on left side of external pipe
  - `$!>(cmd)` in pipe chain
  - `$(cmd) | $!(cmd)` — valid, no diagnostic expected (regression guard)
- [ ] **Language Spec** — update pipe section with strict semantics and passthrough restriction
- [ ] **Build & test** — `dotnet test` zero failures

---

## 6. Out of Scope

- Per-stage strict checking in the VM (intermediate stage exit codes triggering `CommandError`). The companion word bit 0 is already reserved for this; a future spec can enable it without opcode changes.
- `$>(cmd1 | cmd2)` behavior inside passthrough — the lexer does not split on `|` inside passthrough, so the literal string `cmd1 | cmd2` is passed as the program argument. If the program is `sh` or `bash`, the shell interprets the pipe. This is existing behaviour, not changed here.
- SA rule for intermediate `$!()` stages (false positive problem described in §2.2).
- Changing `ExecPipelineStreaming` — no changes to the streaming execution pipeline.
