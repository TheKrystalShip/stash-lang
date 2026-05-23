# String Spread — Stdlib-Only Word Splitting (`str.words`, `str.shellSplit`)

**Status:** Mini-spec — backlog
**Created:** 2026-05-06
**Revised:** 2026-05-09 — pivoted from "overload `...str` as whitespace-splat" to "stdlib helpers only, `...str` stays unsupported." Decision Log entry added.
**Companion to:** [Safe Shell Interpolation — Sugar Over process.exec.md](Safe%20Shell%20Interpolation%20—%20Sugar%20Over%20process.exec.md)

## 1. Motivation

Once interpolation values inside `$(...)` stop being whitespace-tokenized (parent spec), code that previously relied on the implicit split needs an explicit migration:

```stash
let opts = "-la --color=always";
$(ls ${opts});               // BEFORE: 2 args. AFTER (parent spec): 1 arg, ls fails.
```

The same friction shows up in plain function calls today:

```stash
fn run(prog: string, ...args: string) { ... }
let flags = "-v --quiet";
run("mytool", ...flags);     // doesn't exist; today users have to split manually
```

The user needs **one obvious, explicit way** to turn a flags-string into argv. This spec defines that way: a pair of stdlib helpers (`str.words`, `str.shellSplit`) that compose with the existing array-spread operator. No new spread semantics, no overloading.

## 2. Decision (locked)

| Question                                                | Decision                                                                                                                                                              |
| ------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Does `...str` (spread on a string) get special meaning? | **No.** Spreading a string is **not supported**. The runtime errors with `"Cannot spread <type>; expected array."` (today's behavior — unchanged).                    |
| Is the `...str` syntax slot reserved?                   | **Yes — reserved for future use.** If Stash ever grows char-iterable strings (`for ch in s`, `arr.from(s)`), `...str` would naturally splat characters, matching JS / Python. We deliberately leave that door open.                                                                              |
| How do users splat a flags-string into argv?           | **`...str.words(s)`** for naive whitespace splitting; **`...str.shellSplit(s)`** for POSIX-style quote-aware splitting. Both return `array<string>`, composing with the existing array-spread.                                                                                              |
| Does `...null` splice zero elements?                    | **Yes** — orthogonal to this spec, but locked here. `f(...null) ≡ f()`. (See §6.)                                                                                     |

This is the **conservative, reversible** path. Sigil sugar can always be added later. Un-overloading a sigil after release cannot.

## 3. Why Not Overload the Spread? (Alternatives Considered)

The earlier draft of this spec proposed defining `...str` as whitespace-splat. After weighing it, that direction is rejected. Briefly:

| Option | Sketch | Why rejected |
| ------ | ------ | ------------ |
| **B. `...str` = whitespace-splat** | `f(...flags)` splits on whitespace and splices argv. | Permanent divergence from JS/Python (which splat characters). Closes the door on a future char-iterable string consistently. Saves five characters at the call site. The asymmetry argument is decisive: **adding sigil sugar later is easy; un-overloading is impossible.** |
| **C. New sigil — e.g. `...$str` or `~str`**       | Dedicated splat-as-shell-words operator. | New punctuation for a problem one stdlib call already solves. High cost / low payoff; revisit only if the helper form proves genuinely painful in real code. |
| **D. Auto-split inside `$(...)` only** | Strings interpolated into `$(...)` slots with a marker auto-split. | Re-introduces the whitespace-tokenization the parent spec just removed. Two rules for one syntax. Confusing. |
| **E. Lint-only nudge** | Don't add anything; warn when a string is spread. | Doesn't help users — there's no canonical "right answer" to point them at. (This spec's helpers fix that even without overloading the spread.) |

The asymmetry between Options A and B is the load-bearing argument: **B locks in a JS/Python divergence forever; A leaves the design space open and costs nothing today beyond five extra characters at the call site.**

## 4. Current State of Spread in Stash

`SpreadExpr` already exists in the AST. The `...` token is parsed in:

- Function call argument lists ([`Parser.cs` line 2191](../../../Stash.Core/Parsing/Parser.cs#L2191))
- Array literal element lists
- Destructuring patterns (rest element — different feature, unaffected)
- `$(...)` interpolation slots (added by the parent safe-shell-interpolation spec)

The runtime ([`VirtualMachine.Collections.cs` line 488](../../../Stash.Bytecode/VM/VirtualMachine.Collections.cs#L488)) implements spread by wrapping the value in a `SpreadMarker` and unpacking it during call dispatch ([`VirtualMachine.Functions.cs` line 661](../../../Stash.Bytecode/VM/VirtualMachine.Functions.cs#L661)). Today the unpacker accepts only `List<StashValue>` and throws `"Spread argument must be an array"` for anything else.

**This spec does not change spread dispatch for arrays.** It adds two stdlib functions; users compose them with the existing array-spread (`...str.words(s)`).

## 5. The Helpers

```stash
// Naive Unicode-whitespace tokenization. Empty entries dropped.
// Equivalent to .NET's string.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).
str.words(s: string) -> array<string>

// POSIX-shell-style tokenization that honors single and double quotes.
// Throws ValueError on unterminated quotes.
str.shellSplit(s: string) -> array<string>
```

### 5.1 `str.words` semantics

Split on consecutive runs of Unicode whitespace, dropping empty results.

| Input                          | Output                       |
| ------------------------------ | ---------------------------- |
| `"-la"`                        | `["-la"]`                    |
| `"-la /tmp"`                   | `["-la", "/tmp"]`            |
| `"-la   /tmp"` (multi-space)   | `["-la", "/tmp"]`            |
| `"-la\t/tmp\n"` (tab/newline)  | `["-la", "/tmp"]`            |
| `""`                           | `[]`                         |
| `"   "`                        | `[]`                         |
| `"single"`                     | `["single"]`                 |

Quote characters are **literal**:

```stash
str.words(`-l "long arg" -v`);   // ["-l", "\"long", "arg\"", "-v"]
```

### 5.2 `str.shellSplit` semantics

POSIX-shell-like word-splitting with quote handling:

- Whitespace separates tokens (same Unicode-whitespace rule as `str.words`).
- Double quotes (`"..."`) and single quotes (`'...'`) group their contents into a single token.
- Mixed forms within a token concatenate: `a"b c"d` → `["ab cd"]`.
- Backslash escapes outside of single quotes: `\"` is a literal `"`, `\\` is a literal `\`. Inside single quotes everything is literal.
- An unterminated quote raises `ValueError("unterminated quote in str.shellSplit")`.

| Input                                            | Output                                |
| ------------------------------------------------ | ------------------------------------- |
| `grep "hello world" file.txt`                    | `["grep", "hello world", "file.txt"]` |
| `python -c 'print("hi")'`                        | `["python", "-c", `print("hi")`]`     |
| `a"b c"d`                                        | `["ab cd"]`                           |
| `\"escaped\"`                                    | `["\"escaped\""]`                     |
| `oops "unterminated`                             | **throws** `ValueError`               |

### 5.3 Composition with spread

Both helpers return ordinary `array<string>`, so existing array-spread works:

```stash
let opts = "-la --color=always";
$(ls ${...str.words(opts)});                      // process.exec("ls", ["-la", "--color=always"], ...)

let line = `-e "import sys" -c print`;
$(python ${...str.shellSplit(line)});             // process.exec("python", ["-e", "import sys", "-c", "print"], ...)

fn run(prog: string, ...args: string) { ... }
let flags = "-v --quiet";
run("mytool", ...str.words(flags));               // run("mytool", "-v", "--quiet")
```

## 6. Spread of Other Operands (orthogonal but locked)

Documented here for completeness — these are not new, but the table in earlier drafts referenced them:

| Operand type | `...x` behavior                                                                |
| ------------ | ------------------------------------------------------------------------------ |
| `array`      | Splice elements (existing behavior, unchanged).                                |
| `string`     | **Error.** `"Cannot spread <type>; expected array."` Use `str.words(s)` / `str.shellSplit(s)` and spread the result. |
| `null`       | **Zero entries spliced.** `f(...null) ≡ f()`. Symmetric with empty-array splat and Stash's general null-tolerance pattern for collection-like ops. |
| Everything else (dict, struct, int, bytes, etc.) | Throws `TypeError`: `"Cannot spread <type>; expected array."` |

The `...null` rule is independent of this spec but is finalized here so the runtime change (accept `null` as zero-entry splat) lands together with the helpers.

## 7. Examples

```stash
// Migration of a string-of-flags pattern after the parent spec lands:
let opts = "-la --color=always";
$(ls ${...str.words(opts)});

// Mixing with literals:
let common = "--verbose --color=always";
$(myapp ${...str.words(common)} run --port 8080);
// program = "myapp"
// args    = ["--verbose", "--color=always", "run", "--port", "8080"]

// Function call:
fn render(template: string, ...vars: string) { ... }
let placeholders = "name=Alice age=30 role=admin";
render("greeting.tpl", ...str.words(placeholders));
// render("greeting.tpl", "name=Alice", "age=30", "role=admin")

// Empty / null tolerance:
let extra = env.get("EXTRA_FLAGS") ?? "";    // empty by default
$(builder ${...str.words(extra)} target);    // 0 extra args when env unset

// Quote-aware splitting:
let line = `-e "import sys" -c print`;
$(python ${...str.shellSplit(line)});
```

## 8. Implementation Sketch

| Layer    | Change                                                                                                                                                                                                                                            |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Lexer    | None.                                                                                                                                                                                                                                             |
| Parser   | None.                                                                                                                                                                                                                                             |
| AST      | None.                                                                                                                                                                                                                                             |
| Compiler | None.                                                                                                                                                                                                                                             |
| VM       | One small change: the `SpreadMarker` unpacker (`ExecuteCallSpread` in `VirtualMachine.Functions.cs` and the array-literal spread in `VirtualMachine.Collections.cs`) now also accepts `null` (zero elements). String operands continue to throw, with the message updated to recommend `str.words` / `str.shellSplit`: `"Cannot spread string; use str.words(s) or str.shellSplit(s) and spread the result."` |
| Stdlib   | Add `str.words(s: string) -> array<string>` (single call to .NET `string.Split((char[]?)null, RemoveEmptyEntries)`). Add `str.shellSplit(s: string) -> array<string>` — POSIX-style quote-aware tokenizer, throws `ValueError` on unterminated quotes. |
| Analyzer | None required. (Optional polish: recognize the common pattern `...str.split(s, " ")` and suggest `...str.words(s)` — file as follow-up if useful.)                                                                                                |
| Docs     | Standard library reference: document `str.words` and `str.shellSplit` under the `str` namespace. Language spec: in the spread/rest section, add a note that spread on a string is not supported and direct readers to the two helpers; document the `...null` zero-entry rule. |
| Tests    | `str.words` unit tests (empty, whitespace-only, multi-whitespace, tab/newline, Unicode whitespace, embedded quotes are literal). `str.shellSplit` unit tests (single/double quotes, mixed, backslash escapes, unterminated quote → `ValueError`). VM tests: `...null` produces zero entries in (a) function calls, (b) array literals, (c) `$(...)` slots. VM tests: `...string_value` throws `TypeError` with the updated message. |

## 9. Tooling-Compatibility Checklist

Per `.claude/language-changes.md`:

| Component             | Impact                                                                                              |
| --------------------- | --------------------------------------------------------------------------------------------------- |
| **Documentation**     | `docs/Stash — Standard Library Reference.md`: add `str.words`, `str.shellSplit`. `docs/Stash — Language Specification.md`: spread section — note string is not spreadable; document `...null`. |
| **LSP**               | Completion + hover for the two new `str` functions — handled automatically once they're registered in `Stash.Stdlib/BuiltIns/StrBuiltIns.cs` (existing reflection-based metadata pipeline). No semantic-token or diagnostic changes. |
| **DAP**               | No impact — no new types, no new value shapes.                                                      |
| **Playground**        | Monarch tokenizer: no change (no new keywords). Sandbox: `str` namespace is already permitted.       |
| **VS Code extension** | TextMate grammar: no change.                                                                        |
| **Static analysis**   | No new resolver visitors required. (Optional follow-up rule in §8 Analyzer.)                        |
| **Examples**          | Add or update `examples/` script (e.g. `shell_args.stash`) demonstrating `...str.words(opts)` and `...str.shellSplit(line)` inside `$(...)`. |
| **Tests**             | xUnit coverage for the two helpers and the `...null` zero-splat path (see §8 Tests).                |

## 10. Open Questions

1. **Should there be an `str.shellSplit` Windows mode?** POSIX rules differ from `cmd.exe` quoting. For now, stick to POSIX rules everywhere; document explicitly. Users on Windows who want `cmd.exe` semantics can compose manually or wait for a follow-up `str.shellSplit(s, mode: "posix" | "cmd")` if real demand appears.
2. **Should `bytes` be spreadable?** Out of scope here. Revisit if a real use case appears.
3. **Should `dict` be spreadable into key/value pairs?** Out of scope; that's a separate object-spread / merge spec.

## 11. Decision Log

| Date       | Decision                                                                                          | Chosen                                                                                                | Alternatives considered                                                                  |
| ---------- | ------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| 2026-05-06 | (Earlier draft) Define `...str` as whitespace-splat?                                              | Initially **yes**.                                                                                    | Stdlib helper only; new sigil; `$(...)`-only auto-split.                                 |
| 2026-05-09 | **Reversed.** Define `...str` as whitespace-splat?                                                | **No.** Stdlib helpers only (`str.words` / `str.shellSplit`). `...str` stays unsupported / reserved.  | Whitespace-splat (Option B); new sigil (Option C); auto-split in `$(...)` (Option D); lint-only (Option E). Asymmetry argument decisive: adding sigil sugar later remains possible; un-overloading does not. |
| 2026-05-09 | What counts as whitespace for `str.words`?                                                       | Unicode whitespace via `string.Split((char[]?)null, RemoveEmptyEntries)`.                             | ASCII / POSIX `IFS`.                                                                     |
| 2026-05-09 | `str.shellSplit` quoting rules?                                                                   | POSIX shell — `"..."`, `'...'`, backslash escapes outside single quotes; `ValueError` on unterminated quote. | Match `cmd.exe`; mode parameter (deferred — see §10).                                |
| 2026-05-09 | `...null` behavior?                                                                               | Splice zero entries.                                                                                  | Throw `TypeError`.                                                                       |
