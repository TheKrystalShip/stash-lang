# Safe Shell Interpolation — Sugar Over `process.exec`

**Status:** Design — backlog
**Created:** 2026-05-06
**Supersedes:** [Unique Language Concepts — Volume 3.md](Unique%20Language%20Concepts%20—%20Volume%203.md) §6 ("Safe Shell Interpolation — `safe$(...)` or Auto-Quote in `$(...)`")

## 1. Motivation

The V3 brainstorm framed shell-interpolation safety as a problem to be solved with a new sigil (`safe$(...)`). After investigation, two facts reframe the problem:

1. **Stash already does not use `/bin/sh -c`.** Commands launched by `$(cmd)` go through `Process.Start` with `UseShellExecute = false` and a discrete `ArgumentList`. The runtime [`CommandParser`](../../../Stash.Core/Common/CommandParser.cs) is essentially a quote-aware whitespace splitter — it does not interpret `;`, `|`, `&&`, `$()`, backticks, or any other shell metacharacters. Pipes and redirects are parsed at source level (`PipeExpr`, `RedirectExpr`) and never appear inside a runtime command string.

2. **The remaining injection vector is narrower than V3 implied.** It is **argv-splitting plus glob/tilde injection on unquoted interpolation values**. An attacker who controls a value cannot run arbitrary commands, but can:
   - Inject extra argv entries (turning `$(rm ${x})` into `rm -rf /` if `x = "-rf /"`).
   - Inject glob patterns that expand to file lists (`x = "*"` → all files in cwd).
   - Inject tilde expansion (`x = "~"` → home directory path).
   - Break out of source-level quoting if the value contains a literal `"` (turning `$(rm "${x}")` into `rm -rf /` if `x = `foo" "-rf" "/`).

This means the V3 framing — _"user types `$(rm -rf ~)` and the script executes it"_ — is already wrong for Stash today. The correct framing is "argv injection in a no-shell environment." That makes a sigil-based fix feel heavy for the actual risk surface.

> **Decision (locked):** Address argv + glob + tilde injection. Do _not_ introduce a new command sigil.

## 2. Design Goals

| #   | Goal                                                                                                                                  |
| --- | ------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | Eliminate argv-injection, glob-injection, and tilde-injection from interpolated values.                                               |
| 2   | No new `$`-sigil syntax. Reuse the existing `$(...)`, `$!(...)`, `$>(...)`, `$!>(...)`, `$<(...)`, `$!<(...)` surface unchanged.      |
| 3   | Make the standard library the single source of truth: `$(...)` becomes _sugar_ over a stdlib `process.exec`-style API. (Mirrors how shell `cd` is sugar over `env.chdir`.) |
| 4   | Behavior is predictable: tokenization (whitespace splitting, glob, tilde, source-level quote stripping) applies _only_ to literal text in source; interpolation slots are passed through verbatim as argv elements. |
| 5   | Arrays splat naturally into multiple argv slots so list-of-args is ergonomic.                                                         |

## 3. The Architectural Move — Sugar Over Stdlib

`$(...)` desugars at **compile time** to a call to a stdlib function, in the same way that the shell-mode `cd` line is sugar for `env.chdir(...)`. The standard library becomes the source of truth for command execution; the `$(...)` family is purely surface syntax.

### 3.1 Stdlib API surface (proposed)

```stash
// All six sigils desugar into a single overload set, parameterized by mode + strict:
process.exec(
    program: string,
    args: array<string>,
    opts: ExecOptions = ExecOptions{}
) -> CommandResult            // capture mode, lenient

process.execStrict(...)       // throws CommandError on non-zero exit (or use opts)
process.execPassthrough(...)  // inherits stdio
process.execStream(...)       // returns StreamingProcess

// Pipes desugar to:
process.pipeline(
    stages: array<PipelineStage>,
    opts: ExecOptions = ExecOptions{}
) -> CommandResult

struct PipelineStage {
    program: string,
    args: array<string>
}

struct ExecOptions {
    mode: ExecMode = ExecMode.Capture,   // Capture | Passthrough | Stream
    strict: bool = false,
    redirect: RedirectSpec? = null,      // for source-level > / >> / 2>
    // ...env vars, cwd, etc. as needed
}
```

> Concrete naming (one function with options vs. multiple) is an implementation detail; what matters is that the surface is finite, explicit, and has signatures that take **arrays of arguments, not strings**.

### 3.2 Desugaring table

| Source                                       | Desugars to                                                                              |
| -------------------------------------------- | ---------------------------------------------------------------------------------------- |
| `$(ls -la)`                                  | `process.exec("ls", ["-la"], { mode: Capture })`                                         |
| `$(ls -la ${dir})`                           | `process.exec("ls", ["-la", dir], { mode: Capture })`                                    |
| `$(ls ${...flags} ${dir})`                   | `process.exec("ls", [...flags, dir], { mode: Capture })`                                 |
| `$!(make build)`                             | `process.exec("make", ["build"], { strict: true })`                                      |
| `$>(apt install -y ${pkg})`                  | `process.exec("apt", ["install", "-y", pkg], { mode: Passthrough })`                     |
| `$<(tail -f ${log})`                         | `process.exec("tail", ["-f", log], { mode: Stream })`                                    |
| `$(grep ${pat} \| wc -l)`                    | `process.pipeline([Stage("grep",[pat]), Stage("wc",["-l"])], {})`                        |
| `$(make build > ${out})`                     | `process.exec("make", ["build"], { redirect: RedirectSpec(out, ...) })`                  |

Each `CommandExpr` in the AST is lowered by the **compiler**, not the VM. The existing command opcodes can either be retained (compiling to them under the hood) or replaced with the namespace-call opcode emitted for `process.exec` — that is a downstream implementation choice, but the source-of-truth is the stdlib signature.

## 4. Tokenization Model

The whole semantic shift hinges on **what gets tokenized when**.

### 4.1 Compile time only

Tokenization (whitespace splitting, source-level quote grouping) operates on **literal text spans** in the source `$(...)` body. It is performed once, at compile time, by the Stash compiler — never on runtime values.

### 4.2 Source-level quotes group literal tokens

Source quotes inside `$(...)` are pure compile-time grouping markers. They are stripped before the literal token reaches argv:

```stash
$(grep "hello world" file.txt)
// program = "grep", args = ["hello world", "file.txt"]
```

Quotes never affect interpolation slots — slots are always whole argv elements regardless of surrounding quotes.

### 4.3 Interpolation slots are atomic argv entries

A `${expr}` slot becomes a single argv element. The value's stringification is _the_ argv element. No splitting on its inner whitespace, no `"`-escape interpretation, nothing.

```stash
let userPath = "; rm -rf ~";
$(ls ${userPath});
// program = "ls", args = ["; rm -rf ~"]    // single literal arg, ls fails to find that file
```

The runtime `CommandParser` is **not invoked on interpolation values**. Quotes the user puts around an interpolation (`"${x}"`) are now redundant — the value is already a single argv entry without them. The compiler can warn on redundant quoting (see §7).

### 4.4 Glob and tilde apply only to literal tokens

Glob (`*`, `?`, `[...]`) and tilde (`~`) expansion operate only on argv entries that originated from **literal source text**. Interpolated values are never glob-expanded, never tilde-expanded.

| Source                                 | Result                                                  |
| -------------------------------------- | ------------------------------------------------------- |
| `$(ls *.log)`                          | Glob runs (literal token).                              |
| `let p = "*.log"; $(ls ${p})`          | No glob — `ls` receives literal `"*.log"`.              |
| `$(ls ~)` / `$(ls ~/Downloads)`        | Tilde runs (literal token).                             |
| `let p = "~/Downloads"; $(ls ${p})`    | No tilde — `ls` receives literal `"~/Downloads"`.       |

If a user wants glob/tilde on a value, they must either:

- Use the literal form (`$(ls *.log)`).
- Call the explicit stdlib helpers (e.g. `fs.glob`, `path.expand`) and pass the result.

> **Rationale:** Predictability. Today's silent glob/tilde-on-interpolation is a footgun even when not adversarial — a value containing `*` causing accidental file expansion has bitten every shell user.

### 4.5 Program-name slot

The first slot — whether literal or interpolated — is a **single literal program name**. No tokenization.

```stash
let tool = "ls";        $(${tool})           // runs "ls"
let tool = "ls -la";    $(${tool})           // tries to exec a binary named "ls -la" — fails
let cmd = ["ls", "-la"]; $(${cmd})            // implicit splat: program = "ls", args = ["-la"]
```

The third form falls out of the splat rule (§5) automatically.

## 5. Array Splatting

When an interpolation value is an **array**, its elements are spliced into the argv list as separate entries (implicit splat).

```stash
let flags = ["-la", "--color=always"];
$(ls ${flags} /tmp);
// program = "ls", args = ["-la", "--color=always", "/tmp"]
```

For clarity, an explicit spread form is also supported and is the analyzer's recommended style:

```stash
$(ls ${...flags} /tmp);   // explicit splat — preferred
```

A static-analysis rule (see §7, SA08xx) suggests promoting implicit-splat sites to the explicit form when the interpolation's static type is known to be an array.

Scalars (`string`, `int`, `bool`, etc.) become a single argv element via standard stringification.

## 6. Examples — Before / After Semantics

### 6.1 The motivating bug, fixed by default

```stash
let userInput = "; rm -rf ~";

// Today and after the change — both safe (no shell):
$(ls ${userInput});

// What changes: the value never participates in tokenization.
//   Today:  CommandParser sees "ls ; rm -rf ~" → tokens [ls, ;, rm, -rf, ~]
//           (4 spurious args; ~ tilde-expanded to $HOME — argv injection)
//   After:  args = ["; rm -rf ~"] — single literal argv entry, ~ NOT expanded
```

### 6.2 Quote-escape attack

```stash
let userPath = `foo" "-rf" "/`;

// Today: $(rm "${userPath}") becomes `rm "foo" "-rf" "/"` after string assembly.
//        CommandParser tokenizes to [rm, foo, -rf, /]. CATASTROPHIC.
// After: args = [`foo" "-rf" "/`] — single literal argv entry. rm fails to find that file.
$(rm "${userPath}");   // safe (and the "..." around the slot is redundant; SA warns)
$(rm ${userPath});     // equally safe
```

### 6.3 Pipes with interpolation

```stash
let pattern = "ERROR.*timeout";
let log     = "/var/log/app.log";
$(grep ${pattern} ${log} | wc -l);
// Desugars to:
//   process.pipeline([
//       Stage("grep", [pattern, log]),
//       Stage("wc",   ["-l"])
//   ], {});
```

### 6.4 Splat patterns

```stash
let kubectl = ["kubectl", "--context=prod", "--namespace=app"];
let extra   = ["-l", "app=web", "-o", "wide"];

$(${...kubectl} get pods ${...extra});
// program = "kubectl", args = ["--context=prod", "--namespace=app", "get", "pods", "-l", "app=web", "-o", "wide"]
```

### 6.5 What breaks (intentionally)

```stash
// (a) Building flags as a single string no longer works — it's now ONE argv entry, not two.
let opts = "-la /tmp";
$(ls ${opts});                  // BEFORE: ls -la /tmp     AFTER: ls receives "-la /tmp" as one arg → fails.
$(ls ${...str.split(opts, " ")});   // The explicit migration.

// (b) Globbing a value no longer works.
let pattern = "*.log";
$(ls ${pattern});               // BEFORE: globs           AFTER: passes literal "*.log".
$(ls ${...fs.glob(pattern)});   // Migration.

// (c) Tilde in a value no longer expands.
let home = "~/.config";
$(cat ${home});                 // BEFORE: cat /home/user/.config    AFTER: cat receives "~/.config".
$(cat ${path.expand(home)});    // Migration.

// (d) Interpolated program string with a space no longer tokenizes.
let cmd = "git status";
$(${cmd});                      // BEFORE: runs git with arg "status"    AFTER: tries to exec "git status" — fails.
$(${...str.split(cmd, " ")});   // Or restructure to `let parts = ["git","status"]; $(${...parts});`.
```

## 7. Static Analysis

| Code     | Severity | Rule                                                                                                                                                       |
| -------- | -------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| SA08xx-A | Warning  | Redundant source-level quoting around an interpolation slot: `"${x}"` → `${x}`. The quotes had a security purpose under the old semantics; under the new semantics they are inert. |
| SA08xx-B | Info     | Implicit array splat detected (`${arr}` where `arr` has static array type). Suggest the explicit `${...arr}` form for readability.                         |
| SA08xx-C | Warning  | Likely-broken migration: a string literal with whitespace assigned to a variable then interpolated as a single slot (e.g. `let opts = "-a -b"; $(...${opts}...)`). Suggest `str.split(...)` + splat or restructuring as an array literal. |

> **Deprecation strategy (locked):** No transitional runtime shim. Behavior changes at the release boundary; the change is announced in CHANGELOG, in a migration note, and surfaced by SA08xx-C wherever the analyzer can plausibly detect it.

## 8. Interaction With Existing Features

### 8.1 The six sigil variants

All six (`$()`, `$!()`, `$>()`, `$!>()`, `$<()`, `$!<()`) survive unchanged at the **syntax level**. They differ only in the `mode` and `strict` parameters passed to the desugared call. Streaming and strict semantics are unaffected.

### 8.2 Pipes (`PipeExpr`)

Pipe chains desugar to `process.pipeline(stages, opts)`. Each stage carries its own program + args list, lowered by the same rules. Streaming-inside-pipe-chain remains rejected (SA0710/0711 already covers this).

### 8.3 Redirects (`RedirectExpr`)

Source-level `> file`, `>> file`, `2> file`, `< file` lower into a `RedirectSpec` field on `ExecOptions`. The redirection target itself follows the same interpolation rules — `$(make build > ${out})` passes `out` as a literal pathname.

### 8.4 Strict mode (`$!(...)`)

Unchanged. `strict: true` in opts; throws `CommandError` on non-zero exit.

### 8.5 Streaming (`$<(...)`)

Unchanged. `mode: Stream` in opts; returns `StreamingProcess`. The `$<(a | b | c)` form continues to use source-level pipe stages with the streaming flag on the last stage (which is how it works today).

### 8.6 Capability sandbox (V3 §1)

Orthogonal. Whatever capability checks `process.exec` performs apply identically to desugared `$(...)` calls. In fact, having a single stdlib chokepoint makes capability enforcement _simpler_ to reason about — there is one place that checks `--allow-run`.

### 8.7 `secret` type (V2)

Orthogonal but improved. Today, `$(curl -H "Authorization: Bearer ${apiKey}")` builds the argument as a single argv entry by string concatenation in the source. Under the new model, the same source code lowers to `process.exec("curl", ["-H", "Authorization: Bearer ${apiKey-stringified}"])` — exactly the same argv. Secret redaction in error messages continues to work because `process.exec` sees the argv.

## 9. Implementation Sketch

| Layer         | Change                                                                                                                                          |
| ------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| Lexer         | None. `$(`, `$!(`, `$>(`, `$!>(`, `$<(`, `$!<(` already exist.                                                                                  |
| Parser / AST  | None. `CommandExpr`, `PipeExpr`, `RedirectExpr` already carry the parts (literal segments + interpolation expressions).                         |
| Compiler      | New lowering pass over `CommandExpr`: walk parts, classify as literal-text vs. interpolation-slot, tokenize literals (whitespace + source-quote grouping + glob + tilde), splat array slots, emit a call to the appropriate `process.*` stdlib function. |
| Stdlib        | Add `process.exec(program, args, opts)` and `process.pipeline(stages, opts)` (or the equivalent minimal surface). Strict/passthrough/streaming are options on the call. |
| VM            | The existing command opcodes can be retained (the new desugaring may emit them as a fast path) or removed in favor of namespace-call dispatch. Implementation detail; either is acceptable as long as the stdlib signatures are the source of truth. |
| Analyzer      | Add SA08xx-A/B/C rules.                                                                                                                          |
| Docs          | Update the language spec §"Command Substitution / Strict Commands / Streaming Command Output" to describe the lowering and the new tokenization model. Update `process` namespace reference. |
| Tests         | New test suite covering: literal tokenization, source-quote grouping, scalar interpolation as single argv, array splat (implicit + explicit), no glob/tilde on values, program-name slot behavior, all six sigils, pipes, redirects. Plus the SA rule tests. |

## 10. Migration Notes (for CHANGELOG)

> **Breaking change.** Interpolated values inside `$(...)` and its variants are now passed as **single literal argv entries**. They are no longer split on whitespace, glob-expanded, or tilde-expanded. Source-level quotes around an interpolation slot (`"${x}"`) are now inert and can be removed.
>
> **You need to migrate** if you currently rely on:
>
> - String values containing whitespace becoming multiple argv entries (`let opts = "-la /tmp"; $(ls ${opts})`).
> - Glob patterns in interpolated values being expanded by the runtime (`let p = "*.log"; $(ls ${p})`).
> - Tilde in interpolated values being expanded to `$HOME` (`let p = "~/foo"; $(cat ${p})`).
> - Interpolated multi-word program names (`let cmd = "git status"; $(${cmd})`).
>
> The compiler now emits warnings (SA08xx-C) for likely-broken sites where it can detect them statically.

## 11. Decision Log

| Decision                                                                                                          | Chosen                                                            | Alternatives considered                                                       |
| ----------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| Solve via new sigil (`safe$(...)`) vs. behavioral change to existing `$(...)`?                                    | Behavioral change                                                 | New sigil (V3 §6 original proposal)                                           |
| What threats to address?                                                                                          | argv injection + glob + tilde injection                           | argv only / document only                                                     |
| Where does the desugaring live?                                                                                   | Compile-time desugar to stdlib `process.exec`                     | Runtime split tracking literal vs. interpolated; or hybrid                   |
| Array-splat marker?                                                                                               | Implicit splat + analyzer suggests explicit `${...arr}`           | Implicit-only (magic); explicit-only (verbose)                                |
| Source-level quotes inside `$(...)`?                                                                              | Group literal tokens at compile time, never affect slot values    | Treat as literal characters; or status quo (runtime quote-aware split)        |
| Glob / tilde on interpolated values?                                                                              | Never expand                                                      | Always expand (back-compat); opt-in marker                                    |
| Program-name slot tokenization when interpolated?                                                                 | Single literal program name                                       | Tokenize first slot; or compile-time error on multi-word values               |
| Migration mechanism?                                                                                              | CHANGELOG + analyzer rules; no runtime shim                       | Analyzer warning → error progression; runtime divergence detection            |

## 12. Open Questions

1. **Stdlib function naming and shape.** Single `process.exec` with options vs. several specialized variants (`exec`, `execStrict`, `execPassthrough`, `execStream`, `pipeline`)? Single + options is more uniform; specialized is more discoverable. Worth checking what `process.*` already exposes before deciding.
2. **Should the existing command opcodes be retired** in favor of compiling everything down to namespace-call dispatch, or kept as a fast path? Performance impact on hot-loop scripts that exec many short commands is the main concern. Resolve once `process.exec` is benchmarked.
3. **`RedirectSpec` shape.** What fields does it need (`stdout`, `stderr`, append, merge `2>&1`, etc.) and how does it interact with the existing `RedirectExpr` AST?
4. **What happens if a user writes `$(${x})` where `x` is `null`?** Likely throw at compile time on the program-name slot if the static type proves null; runtime error otherwise. Confirm error type.
5. **Is the SA08xx-A "redundant quoting" warning ever wrong?** I.e., is there any case under the new semantics where `"${x}"` and `${x}` differ? If not, the rule is safe to enable by default. Sanity-check during implementation.

## 13. Footnote — Why This Is Better Than `safe$(...)`

The V3 proposal added a new sigil that users had to opt into. That is the wrong default — the safe behavior should be free, and the unsafe behavior should require explicit acknowledgment (or, ideally, not be expressible). This design achieves that: the only way to splice arbitrary text into a command line under the new model is to construct it explicitly as part of the literal source, which is no different from any other piece of code review. The interpolation primitive is structurally incapable of injecting argv entries, glob expansion, or tilde expansion. There is no `unsafe$(...)` because there is no "unsafe" mode to opt out of.
