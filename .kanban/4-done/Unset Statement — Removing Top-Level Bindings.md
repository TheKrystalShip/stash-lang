# Unset Statement — Removing Top-Level Bindings

> **Status:** Backlog · Spec
> **Created:** 2026-05-01
> **Authors:** Architect (with user)
> **Kanban:** `0-backlog`
> **Related:** Bare Command Execution — Shell Mode for the REPL (`.kanban/4-done/`)

---

## 1. Motivation

Stash's REPL/shell mode lets users freely declare globals (`let ls = "test";`). Once declared, the
`ShellLineClassifier` treats subsequent occurrences of `ls` as a Stash identifier rather than a
PATH executable, because `VirtualMachine.HasReplGlobal("ls")` returns `true`. There is currently
**no way to remove the binding** without restarting the REPL session. The user must escape the
name with `\ls` for the rest of the session — clumsy, easy to forget, and impossible if the
shadowed name appears inside a pipeline or `$(...)` capture.

The same gap exists in scripts: a top-level `let` cannot be retracted, only reassigned to `null`,
which leaves a "tombstone" binding that still shadows builtins and namespaces and still satisfies
`HasReplGlobal`-style probes.

This spec introduces a top-level `unset` statement that removes the binding outright.

### 1.1 Concrete user stories

```
shell> let ls = "test"             # accidentally shadows the ls binary
shell> ls                          # Stash variable lookup → "test"
shell> unset ls                    # binding removed
shell> ls                          # PATH executable runs
```

```stash
// at script top level
let ls = "test"
unset ls            // ls is no longer defined; subsequent reference is an error
```

```stash
// REPL — clean up multiple temporaries
shell> unset tmp1, tmp2, scratch
```

---

## 2. Design summary (one-paragraph)

`unset name1, name2, …;` is a **top-level-only statement** that removes one or more named
bindings from the current global scope. It is a **soft keyword**, recognised only as a statement
opener so existing user code using `unset` as an identifier continues to compile. Allowed targets
are user-declared `let`, `fn`, `struct`, and `enum` bindings — and `const` bindings **only in the
REPL**. Built-in namespaces, built-in functions, imported modules, and aliases cannot be unset.
Unsetting an unknown name is a static-analysis warning and a runtime no-op. The shell mode needs
**no special sugar**: bare `unset foo` already parses as the language statement.

---

## 3. Scope decisions (locked)

| Question                 | Decision                                                                           |
| ------------------------ | ---------------------------------------------------------------------------------- |
| Where valid              | Top-level only (script global / REPL global). Hard error inside fn/block.          |
| Allowed targets          | User `let`, `fn`, `struct`, `enum`. `const` allowed in REPL only.                  |
| Disallowed targets       | Built-in namespaces, built-in functions, imports, aliases.                         |
| Unknown name             | SA warning (SA0840), runtime no-op.                                                |
| Syntax                   | `unset name1, name2, …;` — comma-separated bare identifiers only.                  |
| Dotted/indexed paths     | **Not supported.** `dict.delete(d, k)` already covers nested removal.              |
| Shell sugar              | None needed — bare `unset foo` IS the statement.                                   |
| Env-var unset            | Separate built-in `env.unset(name)` added in this spec.                            |
| Keyword strength         | Soft keyword (contextual), matching the recent direction (async/await/defer/etc.). |
| `const` unset in scripts | **Hard error** (SA0843). `const` remains an inviolable script-level promise.       |

---

## 4. Grammar

### 4.1 Production

```
UnsetStmt   ::= "unset" UnsetTarget ("," UnsetTarget)* ";"
UnsetTarget ::= IDENTIFIER
```

- The keyword `unset` is contextual: it is recognised as a statement keyword **only** when the
  parser sees `unset` followed by an identifier at a position where a statement is expected. In
  any other position (`let unset = 1;`, `unset()` as a call, `obj.unset` as field access) it is
  a regular identifier.
- At least one target is required. Empty `unset;` is a parse error ("expected identifier after
  'unset'").
- Trailing comma is **not** allowed: `unset a, b,;` → parse error.
- Targets must be bare identifiers. `unset foo.bar;`, `unset foo["bar"];`, `unset (foo);`,
  `unset *;` all → parse error ("'unset' targets must be bare identifiers").

### 4.2 Examples that parse

```stash
unset x;
unset a, b, c;
unset _temp, MyStruct, helper;
```

### 4.3 Examples that do not parse

```stash
unset;                  // missing identifier
unset a,;               // trailing comma
unset foo.bar;          // dotted path
unset foo["k"];         // indexed
unset 42;               // not an identifier
```

### 4.4 Soft-keyword interaction

Following the precedent of `async`, `await`, `defer`, `lock`, `elevate`, `retry`, the
disambiguation lives in `Parser.Statement()`:

```
if (IsUnsetKeyword()) return UnsetStatement();
```

`IsUnsetKeyword()` returns `true` iff the current token is `Identifier("unset")` and the
**next** token is `Identifier(...)`. This ordering check **must come before** `Match(Identifier)`,
exactly as documented in the soft-keyword refactor memory.

`unset` therefore continues to be a perfectly valid identifier in expression position:

```stash
let unset = fn() => print("hi");   // OK — unset is a variable
unset();                           // OK — call to the variable
fn outer() {
    let unset = 1;                 // OK
    unset = unset + 1;             // OK — assignment to unset
}
```

The only incompatibility is a pre-existing top-level statement of the form
`unset IDENT;` where `unset` was meant to be an expression — a vanishingly rare construct that
would have been a no-op expression statement before.

---

## 5. Semantics

### 5.1 Allowed and disallowed targets

| Target kind                    | REPL                               | Script                             | Notes                                                        |
| ------------------------------ | ---------------------------------- | ---------------------------------- | ------------------------------------------------------------ |
| User `let` global              | ✅ removed                         | ✅ removed                         | Primary use case.                                            |
| User `fn` global               | ✅ removed                         | ✅ removed                         | Function value lives in the same `_globals` dict.            |
| User `struct` global           | ✅ removed (+ type registry entry) | ✅ removed (+ type registry entry) | Subsequent `is MyStruct` returns `false`.                    |
| User `enum` global             | ✅ removed (+ type registry entry) | ✅ removed (+ type registry entry) | Subsequent `is MyEnum` returns `false`.                      |
| User `const` global            | ✅ removed                         | ❌ SA0843 (Error)                  | Const remains inviolable in scripts; REPL allows fix-typo.   |
| Imported module (`import x`)   | ❌ SA0842 (Error)                  | ❌ SA0842 (Error)                  | Breaks transitive references; use a fresh REPL session.      |
| Import alias (`import x as y`) | ❌ SA0842 (Error)                  | ❌ SA0842 (Error)                  | Same reason.                                                 |
| Built-in namespace             | ❌ SA0841 (Error)                  | ❌ SA0841 (Error)                  | `arr`, `dict`, `process`, `env`, etc. are runtime fixtures.  |
| Built-in global function       | ❌ SA0841 (Error)                  | ❌ SA0841 (Error)                  | `print`, `len`, `exit`, etc.                                 |
| Unknown / never-declared name  | ⚠ SA0840 (Warning), no-op          | ⚠ SA0840 (Warning), no-op          | Bash precedent; no surprise in REPL.                         |
| Loop variable / fn parameter   | n/a                                | n/a                                | Not at top level. SA0844 (scope error) applies if attempted. |

When multiple targets are listed in one statement and any individual target is invalid
(SA0841/0842/0843), the **diagnostic is per-target** — other targets in the same statement still
proceed. This matches `let a = ..., b = ...` semantics.

### 5.2 What "removed" means at runtime

Globals are stored in `VirtualMachine._globals` (`Dictionary<string, StashValue>`, name-keyed).
Removal is `_globals.Remove(name)`. Side effects:

- `HasReplGlobal(name)` returns `false` immediately. Shell classifier flips back to PATH lookup
  on the next REPL line. **This is the central fix.**
- `EnumerateGlobals()` no longer yields the entry.
- `_constGlobals` (HashSet) is also cleared for that name.
- For struct/enum targets, the corresponding entry in the runtime type registry
  (`StashStructDescriptor` / `StashEnumDescriptor` table) is also removed, so `is MyStruct`,
  pattern matching, and `typeof` lookups stop recognising the type.
- Subsequent reads of the global (via `LoadGlobalSlot` or by name) raise `NameError` —
  semantically identical to accessing any never-declared global.
- The `GlobalSlotAllocator` slot is **not** reclaimed. It becomes a dead slot. A later
  `let name = ...;` re-`GetOrAllocate`s the same name → same slot → correct rebind. This is
  cheap and correct; reclaiming slots would require rewiring every cached `LoadGlobalSlot`.

### 5.3 Closure interaction

Because the feature is restricted to top-level globals, there are no captured upvalues to
worry about — globals are loaded by slot, not captured. A lambda created **before** an `unset`:

```stash
let counter = 0;
let bump = () => counter = counter + 1;
bump();                  // counter = 1
unset counter;
bump();                  // raises NameError: undefined global 'counter'
```

The lambda's bytecode emits a `LoadGlobalSlot` / `StoreGlobalSlot` against the now-empty dict
entry, so the read fails with a regular `NameError`. This matches how every other access of an
undefined global behaves. **No special closure handling is required.**

### 5.4 Re-binding after unset

```stash
let x = 1;
unset x;
let x = "hello";        // OK — fresh declaration, same slot reused
```

This works without special-casing because `_globals.Remove("x")` makes the dict empty for that
key, so the compiler's "redeclaration of existing global" check (which today only fires when the
global is currently in `_globals`) sees nothing and treats the second `let` as a first
declaration. The `GlobalSlotAllocator` returns the cached slot, the new `StoreGlobalSlot` writes
the new value, and reads succeed.

### 5.5 Unsetting the same name twice in one statement

```stash
unset x, x;            // SA0840 on the second 'x', runtime no-op
```

Statically allowed; at runtime the first `Remove` succeeds, the second is a no-op. The analyzer
emits SA0840 ("`x` is not defined") for the duplicate.

### 5.6 Unsetting in dead code (after `return` / `throw` / `exit`)

The CFG's existing unreachable-code rule (SA0010 / equivalent) applies normally. `unset`
statements in dead code are flagged like any other unreachable statement.

### 5.7 No effect on environment, processes, or files

`unset` removes a Stash binding only. It does **not** clear environment variables, kill
subprocesses, close files, or trigger `defer` blocks. (For env vars, see §8.)

---

## 6. Static analysis

### 6.1 New diagnostics

| Code   | Category | Level   | Message                                                                           |
| ------ | -------- | ------- | --------------------------------------------------------------------------------- |
| SA0840 | Bindings | Warning | `'{0}' is not defined; 'unset' has no effect.`                                    |
| SA0841 | Bindings | Error   | `Cannot 'unset' built-in '{0}'.`                                                  |
| SA0842 | Bindings | Error   | `Cannot 'unset' imported binding '{0}'; remove or refactor the 'import' instead.` |
| SA0843 | Bindings | Error   | `Cannot 'unset' 'const' binding '{0}' in a script (allowed in REPL only).`        |
| SA0844 | Bindings | Error   | `'unset' is only valid at the top level of a script or REPL input.`               |

All five live in `Stash.Analysis/Models/DiagnosticDescriptors.cs` under a new "Bindings"
category, following the existing pattern. Suppression follows the standard
`SuppressionDirectiveParser` rules.

### 6.2 Where each rule fires

- **SA0840** — `SemanticValidator.VisitUnsetStmt`: for each target, if the symbol resolver does
  not know the name _and_ (in REPL) the VM does not currently have it as a global. In scripts,
  the analyzer's own symbol table is authoritative. In the REPL, the analyzer must consult the
  VM's live globals (this hook already exists for cross-input symbol resolution; the spec adds
  one new accessor on the analyzer's REPL adapter).
- **SA0841** — for each target, if the resolved symbol is a `BuiltInNamespace` or
  `BuiltInFunction`.
- **SA0842** — for each target, if the resolved symbol is an `ImportBinding` or `ImportAlias`.
- **SA0843** — for each target, if the resolved symbol is a `ConstDeclaration` and the
  compilation context is a script (not REPL). The analyzer already knows the context via its
  `IsRepl` flag (used by other rules).
- **SA0844** — `SemanticValidator.VisitUnsetStmt` early check: if `currentScope.Depth > 0`,
  emit and short-circuit the per-target rules.

### 6.3 Scope tracking after an `unset`

The analyzer's symbol table must model `unset` as a removal so that subsequent references in
the same compilation unit are flagged as undefined. Implementation: in
`SemanticResolver.VisitUnsetStmt`, after validating each target, call
`currentScope.RemoveSymbol(name)`. Subsequent `IdentExpr` resolution then misses, producing the
standard SA0202 "undefined name" diagnostic.

For REPL inputs (each compiled as a separate unit), the analyzer's REPL adapter prunes the
target name from its incremental global symbol set in addition to the live-VM removal that
happens at runtime.

### 6.4 Unused-variable interaction

A `let x = ...` followed only by `unset x;` should **not** trigger the existing
"unused variable" diagnostic. Update the unused-variable rule to consider `unset` a use.
Rationale: writing `let x = expensive(); ...; unset x;` to free a reference is a legitimate
pattern in long-lived scripts.

### 6.5 Const-folding interaction

The compiler tracks compile-time const values via `GlobalSlotAllocator.TrackConstValue`. If a
const is unset in the REPL, calls to `TryGetConstValue` could still return the stale folded
value for **already-compiled** code in earlier REPL inputs. This is acceptable: the older code
captured the value at its own compile time, and re-`let`-ing the name in the REPL would fold a
new value for any new code. The spec recommends adding `GlobalSlotAllocator.RemoveConstValue`
and calling it from the VM's unset path, so future re-compilations don't see the stale const.

---

## 7. Implementation impact

### 7.1 New AST node

`Stash.Core/Parsing/AST/UnsetStmt.cs`:

```
public sealed class UnsetStmt : Stmt
{
    public IReadOnlyList<UnsetTarget> Targets { get; }
    public Token UnsetKeyword { get; }            // for SemanticTokenWalker
    public override StmtType Kind => StmtType.Unset;
    public override SourceSpan Span { get; }
    // visitor dispatch ...
}

public readonly record struct UnsetTarget(string Name, SourceSpan Span);
```

Add `StmtType.Unset` enum value.

### 7.2 Parser

`Parser.Statement()` adds the `IsUnsetKeyword()` check before `Match(Identifier)`.
`UnsetStatement()` consumes the keyword, then parses one-or-more comma-separated identifiers
followed by `;`.

### 7.3 New bytecode opcode

`UnsetGlobal` — opcode number assigned at the end of the current opcode list (next free slot;
implementer to confirm). Encoding: `Ax` form, where `Ax` is the index into the chunk's string
constant pool holding the target name. The compiler emits one `UnsetGlobal` per target. (Listing
multiple names in one source statement does NOT collapse to a single opcode — each removal is
independent so partial errors don't cascade.)

VM dispatch in `VirtualMachine.Dispatch.cs`:

```
case OpCode.UnsetGlobal:
{
    string name = chunk.StringConstants[ReadAx(ip)];
    _globals.Remove(name);
    _constGlobals.Remove(name);
    _structRegistry.Remove(name);   // no-op if not present
    _enumRegistry.Remove(name);
    ReplGlobalAllocator.RemoveConstValue(name);
    break;
}
```

(Exact field names will match the actual VM internals; `_structRegistry` and `_enumRegistry`
above are placeholders for whatever the type registry mechanism turns out to be.)

### 7.4 Compiler

`Stash.Bytecode/Compilation/Compiler.Declarations.cs` — add `VisitUnsetStmt(UnsetStmt s)`:

- Assert `IsTopLevel` (defensive — analyzer should have already errored, but emit a clear
  internal compiler error if reached in non-top-level context, since allowing it would corrupt
  local-frame state).
- For each target: emit one `UnsetGlobal` instruction with the target's name in the string
  constant pool.

### 7.5 Visitor updates (all six)

Every visitor must add a `VisitUnsetStmt` method, per `language-changes.instructions.md`:

- `Compiler` — emits `UnsetGlobal` opcodes (above).
- `SemanticResolver` — validates targets resolve, then removes from scope symbol table.
- `SemanticValidator` — emits SA0840–SA0844 as described in §6.
- `SymbolCollector` — no-op (names being removed aren't being declared).
- `SemanticTokenWalker` — emits keyword token for `unset` and identifier tokens for each target
  (variable / function / struct / enum, matching the original symbol kind).
- `StashFormatter` — formats `unset a, b, c;` with single space after `unset` and `,` followed
  by single space; long lists wrap to one target per line at 80 columns, indented to match the
  `unset` keyword column.

### 7.6 Bytecode serialization

The new opcode is added to the `OpCode` enum and to the writer/reader's opcode table. **No new
metadata kinds** are needed — the name is referenced via the existing string constant pool.

`.stashc` format version bump: this is an additive opcode, so the **bytecode minor version
number** is incremented (consistent with how recent additive opcodes were handled). Older
runtimes loading a chunk that contains `UnsetGlobal` will fail with the standard
"unknown opcode" error.

### 7.7 LSP

- **Completion** — `unset` is offered as a statement-position keyword completion at top level
  only (gated on the `isTopLevelStatementContext` flag the completion provider already
  computes). Inside functions/blocks it is suppressed.
- **Hover** — `unset` shows a tooltip:
  _"Removes one or more top-level bindings. Allowed targets: user-declared `let`, `fn`,
  `struct`, `enum` (and `const` in REPL only)."_
- **Semantic tokens** — `unset` keyword emitted with `TokenTypeKeyword`. Targets emitted with
  the original symbol's token type (variable / function / struct / enum), no special modifier.
- **Diagnostics** — SA0840–SA0844 surface through the existing diagnostic pipeline; no LSP
  changes needed beyond the descriptor registrations.

### 7.8 DAP

No DAP changes required. An `unset` executed during a debug session simply removes a global,
which then drops from any subsequent "Variables" pane refresh in the Globals scope. (The pane
already re-enumerates `EnumerateGlobals()` on each step.)

### 7.9 Formatter

Default style:

```stash
unset x;
unset a, b, c;
unset
    aReallyLongName,
    anotherReallyLongName,
    yetAnother;
```

Wrap rule: if the single-line form exceeds 100 columns, wrap one target per line, indented one
level under `unset`. Trailing semicolon stays on the last target line.

### 7.10 VS Code extension

- TextMate grammar: add `unset` to the `keyword.control.stash` pattern (alongside `defer`,
  `lock`, `elevate`, `retry` after their soft-keyword promotion — those were _removed_ from the
  grammar; for `unset`, since the analyzer always emits a semantic-token override at use sites,
  the grammar entry is **optional**. Recommended: leave it out, let semantic tokens handle it,
  consistent with the soft-keyword precedent).
- `stash-language.js` (Monaco) — same: do NOT add `unset` to the static keyword list; semantic
  tokens cover it.

### 7.11 Playground

The Monaco wrapper picks up the semantic token classification automatically once
`SemanticTokenWalker` emits the keyword token. No additional Playground work needed beyond a
curated example demonstrating the feature.

---

## 8. `env.unset(name)` — separate built-in

A small companion change to round out the user experience:

- **Function:** `env.unset(name: str) -> bool`
- **Behavior:** Removes the environment variable `name` from the current process's environment
  (via `Environment.SetEnvironmentVariable(name, null)`). Returns `true` if the variable
  existed, `false` otherwise.
- **Capability:** Gated on `StashCapabilities.Environment` (same gate as `env.get`/`env.set`).
- **Errors:** `TypeError` if `name` is not a string. `ValueError` if `name` is empty or
  contains `=` or `\0`.
- **Cross-platform:** No special handling — .NET's `SetEnvironmentVariable(name, null)` does
  the right thing on Linux, macOS, and Windows.

This is **not** invoked by the language-level `unset` statement. Bash users who want to clear
env vars must write `env.unset("PATH")` explicitly. This is a deliberate "no magic" choice — see
§3 ("Env-var unset").

A future spec may consider whether to special-case bare `unset FOO` (uppercase identifiers, in
shell mode only) to also try `env.unset` when no Stash binding exists — but that is **not** in
scope here.

---

## 9. Test plan

New file: `Stash.Tests/Bytecode/UnsetTests.cs` for runtime behavior, and
`Stash.Tests/Analysis/UnsetAnalysisTests.cs` for diagnostics.

### 9.1 Runtime tests

| #   | Scenario                                                                                        | Expected                                               |
| --- | ----------------------------------------------------------------------------------------------- | ------------------------------------------------------ |
| 1   | `let x = 1; unset x; x;`                                                                        | `NameError: undefined global 'x'`                      |
| 2   | `let x = 1; unset x; let x = 2; x`                                                              | `2` (rebind succeeds)                                  |
| 3   | `unset undeclared;` (no prior decl)                                                             | runs, returns `null`                                   |
| 4   | `let a=1; let b=2; let c=3; unset a, b; c`                                                      | `3`; `a` and `b` raise NameError                       |
| 5   | `fn f() { return 1; } unset f; f()`                                                             | `NameError`                                            |
| 6   | `struct S { x: int }; unset S; let v = { x: 1 }; v is S`                                        | `false` (type stripped)                                |
| 7   | `enum E { A, B }; unset E; E.A`                                                                 | `NameError`                                            |
| 8   | Lambda captures global, then unset, then call                                                   | `NameError` raised on call                             |
| 9   | REPL: `const PI = 3.14; unset PI; let PI = 3;`                                                  | succeeds (REPL allows const unset)                     |
| 10  | Shell: `let ls = "x"; \`HasReplGlobal("ls")\` → true; `unset ls`; `HasReplGlobal("ls")` → false | flips classifier back                                  |
| 11  | `unset a, a;` (duplicate)                                                                       | runs; second is no-op                                  |
| 12  | `let x = 1; defer print("done"); unset x;`                                                      | defer still runs at scope exit (unset doesn't trigger) |

### 9.2 Static analysis tests

| #   | Scenario                                    | Expected diagnostic        |
| --- | ------------------------------------------- | -------------------------- |
| 1   | `unset undeclared;`                         | SA0840 Warning             |
| 2   | `unset arr;`                                | SA0841 Error               |
| 3   | `unset print;`                              | SA0841 Error               |
| 4   | `import "lib"; unset lib;`                  | SA0842 Error               |
| 5   | `import "lib" as L; unset L;`               | SA0842 Error               |
| 6   | Script: `const C = 1; unset C;`             | SA0843 Error               |
| 7   | REPL: `const C = 1; unset C;`               | no diagnostic              |
| 8   | `fn f() { let x = 1; unset x; }`            | SA0844 Error               |
| 9   | `if true { let x = 1; unset x; }`           | SA0844 Error               |
| 10  | `let x = 1; unset x; print(x);`             | SA0202 Error on `print(x)` |
| 11  | `let x = 1; unset x; let x = 2; print(x);`  | no diagnostic              |
| 12  | Mixed valid/invalid: `unset arr, validVar;` | SA0841 on `arr` only       |
| 13  | `let x = 1; unset x;` (no other use)        | NO unused-var warning      |

### 9.3 Parser tests

| #   | Input             | Expected         |
| --- | ----------------- | ---------------- |
| 1   | `unset;`          | parse error      |
| 2   | `unset a,;`       | parse error      |
| 3   | `unset foo.bar;`  | parse error      |
| 4   | `unset foo["b"];` | parse error      |
| 5   | `let unset = 1;`  | parses (soft kw) |
| 6   | `unset();` (call) | parses (soft kw) |
| 7   | `unset.field;`    | parses (soft kw) |
| 8   | `unset = 5;`      | parses (assign)  |

### 9.4 `env.unset` tests

| #   | Scenario                                   | Expected        |
| --- | ------------------------------------------ | --------------- |
| 1   | `env.set("FOO","x"); env.unset("FOO")`     | returns `true`  |
| 2   | `env.unset("NEVER_SET_VAR")`               | returns `false` |
| 3   | `env.unset("")`                            | `ValueError`    |
| 4   | `env.unset("A=B")`                         | `ValueError`    |
| 5   | `env.unset(42)`                            | `TypeError`     |
| 6   | After `env.unset("FOO")`, `env.get("FOO")` | `null`          |

### 9.5 Cross-platform tests

`env.unset` exercised on Linux/macOS/Windows CI runners. Language-level `unset` is platform-
agnostic — no per-platform behavior to test.

---

## 10. Documentation updates

Per `language-changes.instructions.md`, every language-level addition requires:

1. **`docs/Stash — Language Specification.md`** — new "Unset Statement" section under the
   declarations chapter. Include grammar, allowed targets table, REPL-vs-script differences,
   examples.
2. **`docs/Stash — Standard Library Reference.md`** — `env.unset(name)` entry under the `env`
   namespace.
3. **`docs/Shell — Interactive Shell Mode.md`** — short note that `unset name` is a language
   statement that "just works" in shell mode for removing accidentally-shadowed PATH executables.
   Cross-reference the language spec.
4. **`CHANGELOG.md`** — under "Added" for the language-level statement and `env.unset`. Under
   "Bytecode" for the new `UnsetGlobal` opcode and minor-version bump. No "Breaking Changes"
   entry — this is purely additive.
5. **`examples/`** — add `unset.stash` demonstrating the basic statement, the REPL shadowing
   workaround, and `env.unset`. Wire into `examples/README.md` if present.
6. **VS Code extension** — no syntax-highlighting changes (semantic tokens cover it). Add a
   snippet entry for `unset` if other statements have snippets.
7. **Tree-sitter grammar** (`tree-sitter-stash/`) — add the new `unset_statement` rule.
   Regenerate parser.

---

## 11. Decision log

| Decision                                        | Alternatives rejected                                | Rationale                                                                                              |
| ----------------------------------------------- | ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| Keyword: `unset`                                | `del` (Python), `delete` (JS), `drop`, `forget`      | POSIX/bash precedent; Stash audience is sysadmins; other names carry baggage.                          |
| Top-level only                                  | All scopes / REPL-only / dict-keys-too               | Avoids closure/CFG/definite-assignment nightmares; solves the actual user problem (REPL shadowing).    |
| Soft keyword                                    | Hard keyword                                         | Matches recent direction (async/await/defer/lock/elevate/retry); zero-breakage for existing code.      |
| Comma-separated targets                         | Single-target only / space-separated / function-call | Bash-familiar, parser-unambiguous, supports common multi-cleanup pattern in REPL.                      |
| Bare identifiers only                           | Dotted / indexed                                     | `dict.delete()` already handles nested removal; no second way to do it.                                |
| Unknown target → SA0840 Warning + runtime no-op | Hard error / silent / split scripts vs REPL          | Catches typos in scripts where the analyzer can prove absence; never surprises in the REPL.            |
| Unset of `const` allowed in REPL only           | Always allow / never allow                           | Preserves `const` as a script-level guarantee; concedes REPL ergonomics for fix-typo cases.            |
| No shell sugar layer                            | Add to ShellSugarDesugarer                           | Bare `unset foo` already parses as the language statement; no sugar needed.                            |
| Separate `env.unset(name)`                      | Magical shell-mode env dispatch                      | Explicit > implicit; avoids ambient magic; makes intent readable in scripts.                           |
| New opcode `UnsetGlobal`                        | Synthesize via existing opcodes                      | No clean way to remove a dict entry with current opcode set; one opcode is the smallest honest change. |
| Slot allocator entry kept after unset           | Reclaim slot                                         | Reclaiming would require invalidating cached slot indices in already-compiled code; not worth it.      |
| `LoadGlobalSlot` of unset name → NameError      | Return `null`                                        | Regular semantics — same as any undefined global access; surfaces bugs.                                |
| Re-`let` after unset succeeds (slot reuse)      | Disallow / require new keyword                       | Falls out of the implementation for free; matches user expectation.                                    |

---

## 12. Risks

1. **REPL incremental analyzer must be taught about removal.** If the analyzer's REPL adapter
   keeps a stale entry for a name after `unset`, it will fail to emit SA0202 on subsequent
   references. Mitigation: dedicated test (Analysis test #10) covering the REPL case.
2. **Type registry coupling.** Removing a struct/enum from the runtime registry must be
   coordinated with any caches (interface-resolution, ufcs lookup tables). If those caches
   aren't invalidated, `is MyStruct` could return stale `true`. Mitigation: implementer audit
   of every per-name cache touched at struct/enum declaration time, and a focused test (Runtime
   test #6).
3. **`.stashc` compatibility.** The new opcode means a chunk compiled with the new compiler
   cannot run on an older VM. This is the standard situation for additive opcodes but worth
   noting in the changelog.
4. **User confusion: `unset` does not call `defer`s.** Some users may expect symmetry with
   "going out of scope". Mitigation: documentation emphasis, example file showing this.
5. **Scope creep pressure.** Once `unset` exists, users will request dotted-path support,
   in-function support, env-var integration, etc. The spec has explicit "no" answers; future
   specs must justify changing them.

---

## 13. Out of scope (deliberately)

- Unsetting bindings inside functions or blocks. (See §3.)
- Unsetting dict keys / struct fields. Use `dict.delete(d, k)` and field assignment to `null`.
- Unsetting environment variables via the language-level `unset` statement. Use `env.unset()`.
- Magical shell-mode dispatch where bare `unset FOO` (uppercase) tries env vars when no Stash
  binding exists. Could be a future RFC if there's demand.
- Reclaiming dead global slots in the allocator. Would require cache invalidation across
  compiled chunks; cost > benefit.
- A `defined?(name)` operator or `isDefined(name)` built-in to test whether a binding exists
  before unsetting. Would be a separate small spec if anyone asks.

---

## 14. Implementation checklist (for the Orchestrator)

Mirrors `language-changes.instructions.md` plus the items unique to this spec.

### Core

- [ ] `Stash.Core/Parsing/AST/UnsetStmt.cs` (new)
- [ ] `StmtType.Unset` enum value
- [ ] Parser: `IsUnsetKeyword()` helper, `UnsetStatement()` method, dispatch in `Statement()`
- [ ] Parser tests (§9.3)

### Bytecode

- [ ] `OpCode.UnsetGlobal` (next free number)
- [ ] Compiler: `Compiler.Declarations.cs` `VisitUnsetStmt`
- [ ] VM: `VirtualMachine.Dispatch.cs` case for `UnsetGlobal`
- [ ] `GlobalSlotAllocator.RemoveConstValue(name)` helper
- [ ] Bytecode writer/reader: opcode dispatch entry; minor version bump
- [ ] Runtime tests (§9.1)

### Analysis

- [ ] `DiagnosticDescriptors.cs`: SA0840–SA0844 with "Bindings" category
- [ ] `SemanticResolver.VisitUnsetStmt` — validates + removes from symbol table
- [ ] `SemanticValidator.VisitUnsetStmt` — emits all five diagnostics
- [ ] Update unused-variable rule to count `unset` as a use (§6.4)
- [ ] REPL adapter: new accessor for "is name a current REPL global?"
- [ ] Analysis tests (§9.2)

### Visitors (mandatory per `language-changes.instructions.md`)

- [ ] `Compiler.VisitUnsetStmt` (above)
- [ ] `SemanticResolver.VisitUnsetStmt` (above)
- [ ] `SemanticValidator.VisitUnsetStmt` (above)
- [ ] `SymbolCollector.VisitUnsetStmt` — no-op, returns immediately
- [ ] `SemanticTokenWalker.VisitUnsetStmt` — keyword + identifier tokens
- [ ] `StashFormatter.VisitUnsetStmt` — single-line + wrap form

### Stdlib

- [ ] `EnvBuiltIns.cs`: register `unset` function (gated on `Environment` capability)
- [ ] `env.unset` tests (§9.4)

### LSP

- [ ] Completion: `unset` keyword at top-level statement position only
- [ ] Hover: keyword tooltip text from §7.7
- [ ] No semantic-token changes (walker handles it)

### Tooling

- [ ] Tree-sitter grammar update + regenerate
- [ ] (Optional) snippet entry for `unset` in VS Code extension

### Docs

- [ ] `docs/Stash — Language Specification.md` — new section
- [ ] `docs/Stash — Standard Library Reference.md` — `env.unset` entry
- [ ] `docs/Shell — Interactive Shell Mode.md` — short cross-reference
- [ ] `CHANGELOG.md` — Added entries
- [ ] `examples/unset.stash` (new)

### Verification

- [ ] All new tests pass
- [ ] `dotnet test` clean across full suite
- [ ] Manual REPL smoke test of the §1 motivating user story
- [ ] Cross-platform CI green for `env.unset`
