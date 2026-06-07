# Error Taxonomy — Spec vs Implementation Cross-Reference Ledger

**Purpose.** A decision-neutral cross-reference of Stash's error model — what the Language
Specification says vs. how the code actually behaves — feeding the `language-standard-errors` spec
unit (sealing §"Errors and Cleanup" and the cross-cutting error-type taxonomy).

**Cardinal rule.** Neither spec nor code is presumed correct. Every divergence below is framed as
**two self-consistent worlds** (Candidate Law A = spec wins, Candidate Law B = code wins) with the
concrete consequence of each. Nothing here recommends a resolution; the human rules on each.

**Citations.** `file:line` against the working tree as of 2026-06-07 (branch `main`). Spec =
`docs/Stash — Language Specification.md` (cited by `## header` + line; line numbers are point-in-time).
Generated reference = `docs/Stash — Standard Library Reference.md` (non-normative, derived from code).

**Key navigation facts established up front (used throughout):**

- The registry holds **23** `[StashError]`-attributed types (all in `Stash.Core/Runtime/Errors/`).
- The **base `RuntimeError`** (`Stash.Core/Runtime/RuntimeError.cs:26`), **`AssertionError`**
  (`Stash.Core/Runtime/AssertionError.cs:9`), and **`UserRuntimeError`**
  (`Stash.Core/Runtime/Errors/UserRuntimeError.cs:10`) are **NOT** `[StashError]`-registered.
- The Stash-facing `.type` string for any thrown error comes from
  `BuiltInErrorRegistry.NameOf(RuntimeError)`, emitted by the source generator
  (`Stash.Stdlib.Generators/StashErrorRegistryGenerator.cs:255-258`) as:
  ```csharp
  public static string NameOf(RuntimeError ex) => ex switch {
      UserRuntimeError u => u.UserTypeName,
      _ => _byType.TryGetValue(ex.GetType(), out var n) ? n : ex.GetType().Name,
  };
  ```
  So an **unregistered** type falls back to its **C# class name**: a bare `RuntimeError` surfaces to
  Stash as `.type == "RuntimeError"`; an `AssertionError` as `.type == "AssertionError"`.
- **Catch / `is` matching** is one shared predicate, `ErrorTypeRegistry.Matches(errorType, targetType)`
  (`Stash.Core/Runtime/ErrorTypeRegistry.cs:24-31`), used by **both** the `catch` dispatch
  (`ControlFlow.cs:726`) and the `is`-operator type check (`TypeOps.cs:106`): matches if
  `targetType == "Error"` (base catch-all) **or** exact ordinal string equality `errorType == targetType`. The VM catch dispatch
  (`Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs:695-735`) tries CLR-identity fast-path first
  (built-ins only), then this name fallback. **Catch type-name strings are NOT validated at compile
  time** — `GetTypeNames` (`Stash.Bytecode/Compilation/Compiler.Exceptions.cs:155`) stores raw strings;
  an unknown name compiles and simply never matches.

---

## 1. Error-Type Inventory Table (Direction 1: registry → spec)

Columns: **Reg?** = `[StashError]`-registered · **Desc/Props?** = has `Description` / `Properties`+`PropertyTypes`
metadata · **StdRef?** = appears in generated Standard Library Reference · **Spec?** = named anywhere in the
Language Specification · **Throw sample** = a representative throw site.

| Type | Parent | Reg? | Desc / Props | StdRef? | Spec? | Throw sample (`file:line`) |
| ---- | ------ | ---- | ------------ | ------- | ----- | -------------------------- |
| `RuntimeError` (base) | `System.Exception` | **NO** | n/a | NO (only as `.type` fallback) | **YES** — named as a *thrown type* at §Values L824, L844; named in `@throws`/SA0163 surface | `Stash.Bytecode/VM/VirtualMachine.Variables.cs:44` |
| `UserRuntimeError` | `RuntimeError` | **NO** (carries runtime string name) | n/a | NO | implied by §Errors throw + §Values `throw {type:...}` | `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs:401` |
| `AssertionError` | `RuntimeError` | **NO** | has `Expected`/`Actual` C# fields, NOT exposed via `[StashError]` Props | NO | **YES** — §Equality L730 ("raises `AssertionError`") | `Stash.Stdlib/BuiltIns/AssertBuiltIns.cs` (12 sites) |
| `TypeError` | `RuntimeError` | YES | Desc only | YES | YES — §Functions/Async L1706, L1829 | `Stash.Stdlib/SvArgs.cs:22` (169 sites total) |
| `ValueError` | `RuntimeError` | YES | Desc only | YES | YES — §Errors L2566 example; §Async L1706 | stdlib `Args`/`SvArgs` (109 sites) |
| `IndexError` | `RuntimeError` | YES | Desc only | YES | **NO** | `Stash.Stdlib/BuiltIns/StrBuiltIns.cs:204` (only 6 sites) |
| `IOError` | `RuntimeError` | YES | Desc only | YES | YES — §Async L1706,L1811; §Namespace L2379 | `Stash.Stdlib/BuiltIns/SysBuiltIns.cs:128` (134 sites) |
| `LockError` | `RuntimeError` | YES | Desc + `path` | YES | **NO** (spec §Statements L1563 says lock failure "produces a runtime error", unnamed) | `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs:184` (2 sites) |
| `ReadOnlyError` | `RuntimeError` | YES | Desc only | YES | **YES** (heavily) — §Bindings L1034,L1071,L1080; §Namespace L2347,L2375 | `Stash.Core/Runtime/Types/StashDictionary.cs:38` (28 sites) |
| `TimeoutError` | `RuntimeError` | YES | Desc only | YES | YES — §Async L1842 | `Stash.Stdlib/BuiltIns/NetSocketImpl.cs:228` (25 sites) |
| `CommandError` | `RuntimeError` | YES | Desc + `exitCode`,`stderr`,`stdout`,`command` | YES | named only in §Errors example L2592 (`catch (CommandError e)`); §Shell L2438 describes its fields unnamed | `Stash.Core/Runtime/Errors/CommandError.cs` |
| `StateError` | `RuntimeError` | YES | Desc only | YES | YES — §Async L1797 | `Stash.Core/Runtime/Types/StashStreamingProcess.cs:419` (2 sites) |
| `CancellationError` | `RuntimeError` | YES | Desc only | YES | YES — §Async L1711,L1759,L1823; §Async-event L1969,L2089 | `Stash.Core/Runtime/Types/StashFuture.cs:72` (2 sites) |
| `NotSupportedError` | `RuntimeError` | YES | Desc only | YES | **NO** | `Stash.Core/Runtime/Types/StashStreamingProcess.cs:387` (6 sites) |
| `AliasError` | `RuntimeError` | YES | Desc + `aliasName`,`detail` | YES | **NO** | `Stash.Core/Runtime/AliasRegistry.cs:79` (20 sites) |
| `ParseError` (runtime) | `RuntimeError` | YES | Desc only | YES | **NO** as a runtime type (spec §Runtime L2656 says "parse errors" generically) | `Stash.Core/Runtime/Errors/ParseError.cs` |
| `HostError` | `RuntimeError` | YES | Desc only | YES | **NO** | `Stash.Hosting/Internal/InvokeHostDelegate.cs:81` (9 sites, all in `Stash.Hosting`) |
| `CliAmbiguousOption` | `RuntimeError` | YES | Desc + `option`,`candidates` | YES | **NO** | `Stash.Stdlib/BuiltIns/CliBuiltIns.Parse.cs:1014` (1 site) |
| `CliInvalidValue` | `RuntimeError` | YES | Desc + `option?`,`value`,`expected` | YES | **NO** | `Stash.Stdlib/BuiltIns/CliBuiltIns.Parse.cs:1278` (10 sites) |
| `CliMissingRequired` | `RuntimeError` | YES | Desc + `name` | YES | **NO** | `Stash.Stdlib/BuiltIns/CliBuiltIns.Parse.cs:489` (3 sites) |
| `CliMissingValue` | `RuntimeError` | YES | Desc + `option` | YES | **NO** | `Stash.Stdlib/BuiltIns/CliBuiltIns.Parse.cs:365` (5 sites) |
| `CliSchemaError` | `RuntimeError` | YES | Desc + `field`,`reason` | YES | **NO** | `Stash.Stdlib/BuiltIns/CliBuiltIns.cs:377` (22 sites) |
| `CliUnexpectedPositional` | `RuntimeError` | YES | Desc + `value` | YES | **NO** | `Stash.Stdlib/BuiltIns/CliBuiltIns.Parse.cs:290` (2 sites) |
| `CliUnknownCommand` | `RuntimeError` | YES | Desc + `name`,`candidates` | YES | **NO** | `Stash.Stdlib/BuiltIns/CliBuiltIns.Parse.cs:499` (3 sites) |
| `CliUnknownOption` | `RuntimeError` | YES | Desc + `option` | YES | **NO** | `Stash.Stdlib/BuiltIns/CliBuiltIns.Parse.cs:388` (6 sites) |
| `CliValidationFailed` | `RuntimeError` | YES | Desc + `option?`,`message` | YES | **NO** | `Stash.Stdlib/BuiltIns/CliBuiltIns.Validation.cs:58` (6 sites) |

**Other error-named entities (not in the runtime catch hierarchy — listed for completeness):**

| Entity | File | Role | In catch hierarchy? |
| ------ | ---- | ---- | ------------------- |
| `ParseError` (parser sentinel) | `Stash.Core/Parsing/Parser.cs` | recursive-descent sync sentinel, caught by `Synchronize()`, never propagated to runtime | NO — compile-time only |
| `CompileError` | `Stash.Bytecode/Compilation/CompileError.cs` | compiler diagnostic | NO — compile-time only |
| `StashError` (Hosting record) | `Stash.Hosting/StashError.cs:16` | host-facing DTO surfacing a script error to a C# host (`Kind`/`Message`/`Span`/`CallStack`); also defines synthetic kinds `Cancelled`, `StepLimitExceeded`, `ParseError`, `HostError` | NO — host boundary type, not a Stash-catchable error |
| `StashError` (runtime value) | `Stash.Core/Runtime/Types/StashError.cs:14` | the **first-class Stash error VALUE** a `catch (e)` binds; exposes `.message`/`.type`/`.stack`/`.suppressed` + type props | this IS the caught value |

**Direction-1 headline asymmetry:** all 23 registered types appear in the generated StdRef (uniformly
"Yes"), so the *discriminating* column is **Spec?** — and **11 of 23** registered types
(`IndexError`, `LockError`, `NotSupportedError`, `AliasError`, runtime `ParseError`, `HostError`, and all
**9** `Cli*`) are **named nowhere** in the Language Specification. The spec names a type by name in only
~9 cases, almost all inside the Async section (which was sealed by `language-standard-async`).

---

## 2. Bare-`RuntimeError` / Unregistered Throw-Site Census (Direction 2)

**Total bare `throw new RuntimeError(...)` sites (whole tree minus `Stash.Tests/**`): 351.**
**Total `throw new UserRuntimeError(...)` sites: 8** (all VM-internal rewraps of caught errors, not
user-facing situations — e.g. `ControlFlow.cs:401`, `Functions.cs:863`).
**`Stash.Registry` / `Stash.Registry.Contracts` / `Stash.Registry.Web` contribution: 0.**

These 351 sites all surface to Stash code as `.type == "RuntimeError"` — an **unregistered, undocumented
type name** that is catchable only by the base `catch (e)` / `catch (Error e)` / `is Error`, or by the
literal string `catch (RuntimeError e)` (which works via the name-fallback but is flagged by analyzer
warning **SA0163**, `Stash.Analysis/Models/DiagnosticDescriptors.cs:32`).

### 2a. Site counts by project (site counts, not file counts)

| Project | Bare-`RuntimeError` sites |
| ------- | ------------------------- |
| `Stash.Stdlib` | 149 |
| `Stash.Bytecode` | 144 |
| `Stash.Core` | 52 |
| `Stash.Cli` | 5 |
| `Stash.Hosting` | 1 |
| **Total** | **351** |

### 2b. Situation buckets (indicative keyword classification; a site may fall in one bucket)

| Bucket (kind of failure) | ~count | Representative sites |
| ------------------------ | ------ | -------------------- |
| Type / operand mismatch | ~62 | `Stash.Bytecode/Runtime/RuntimeOps.cs:161,243,268` ("Operands must be numbers…"); `VirtualMachine.Arithmetic.cs:275` |
| Index / bounds | ~32 | `VirtualMachine.Collections.cs:26,38,66`; `Stash.Stdlib/BuiltIns/BufBuiltIns.cs:183,893` |
| Undefined / not-found / no-export | ~15 | `VirtualMachine.Variables.cs:44,53`; `VirtualMachine.Modules.cs:102,340`; `StashInstance.cs:78` |
| Null / empty / value-domain | ~24 | `StashDictionary.cs:169,200` ("key cannot be null"); `StashByteArray.cs:164` ("out of byte range") |
| Import / module | ~14 | `VirtualMachine.Modules.cs:79` (circular), `:102` (not found), `:330` (non-string path), `:316` (no entry point) |
| Division by zero | ~9 | `VirtualMachine.Arithmetic.cs:165,172`; `RuntimeOps.cs:275,283` |
| Const / non-assignable assign | ~9 | `VirtualMachine.Variables.cs:77,90` ("Cannot assign to constant"); `VirtualMachine.TypeOps.cs:506` |
| Struct / field | ~12 | `StashInstance.cs:78,87` ("Undefined field"); `VirtualMachine.TypeOps.cs:355,488` |
| State / precondition | a few | `SftpBuiltIns.cs:495` ("connection invalid or closed"); `TestBuiltIns.cs:194` ("must be used inside test.describe") |
| Arity / argument-count guards (stdlib) | many of the 149 | `TimeBuiltIns.cs:405` ("expects 0 or 1 arguments"); `SysBuiltIns.cs:192` |

**The structural pattern (load-bearing for §3, §4, §5):** the **core VM and runtime-type layer**
(`Stash.Bytecode/VM/*`, `Stash.Bytecode/Runtime/RuntimeOps.cs`, `Stash.Core/Runtime/Types/*`) throws the
**bare base** for virtually every core-language failure — undefined variable, index OOB, dict-key,
div-by-zero, type-mismatch operands, const-assign, struct-field, import. The **stdlib argument-validation
layer** (`Stash.Stdlib/SvArgs.cs`, `Args.*`) throws **typed** `TypeError`/`ValueError`. So the *same
logical failure class* (e.g. "wrong type") is `TypeError` when a stdlib function rejects an argument but
bare `RuntimeError` when the VM rejects an operand. Even within stdlib it is inconsistent: `str.substring`
OOB throws `IndexError` (`StrBuiltIns.cs:204`) while `buf` OOB throws bare `RuntimeError`
(`BufBuiltIns.cs:183`); `http.download` timeout throws bare `RuntimeError` (`HttpBuiltIns.cs:338`), not
`TimeoutError`.

### 2c. The three unregistered types as catch targets

| Type | `.type` surfaced | Catchable by base `catch(e)`/`is Error`? | Catchable by its own name? |
| ---- | ---------------- | ---------------------------------------- | -------------------------- |
| bare `RuntimeError` | `"RuntimeError"` | YES | YES via `catch (RuntimeError e)` string-match — but analyzer **SA0163** warns against it; not a documented public type |
| `AssertionError` | `"AssertionError"` | YES | YES via `catch (AssertionError e)` string-match — works, but the name is undocumented and unregistered |
| `UserRuntimeError` | user's `type` string | YES | YES — by the user-chosen name (this is its whole purpose) |

---

## 3. Spec-Clause Census (Direction 3: every "runtime error"-class phrase, all sections)

Coverage.md's thesis is that the unnamed-error gap is **cross-cutting** (spread across all sections, not
localized to §Errors). The enumeration below **confirms it**: the dominant phrase across the spec is the
**unnamed** "produces a runtime error" / "raises a `RuntimeError`", appearing in §Modules, §Values,
§Bindings, §Expressions, §Statements, §Aggregate, §Namespace, §Shell, §Runtime, and §Errors. A *named
registered type* backs the claim mainly in §Bindings/§Namespace (`ReadOnlyError`) and §Functions-Async
(the already-sealed async unit's named types). 97 total phrase hits; the load-bearing ones:

**Enumeration completeness (all 13 top-level `##` sections checked).** The two sections with **no
runtime-error clause at all** are **§Lexical Structure** (the only hits — L134 `@throws` in a doc-comment
note, L147 the `throw` keyword in the keyword list — are grep false-positives, not error semantics) and
**§Function References**. §Conformance/§Terminology carry only the *generic definition* of "runtime error."
Every other section carries at least one substantive error clause, almost all unnamed.

| Spec § (header) | Line | Phrase | Backed by named registered type? |
| --------------- | ---- | ------ | -------------------------------- |
| Conformance | 55 | "produces a runtime error … must report an error" (normative definition) | N — defines the term generically |
| Terminology | 63 | "**runtime error** means evaluation fails after parsing succeeds" | N — generic definition |
| Source Files and Modules | 320 | "Import cycles produce a runtime error" | **N** (code: bare `RuntimeError`, `Modules.cs:79`) |
| Values and Types | 600 | `Error` listed as a first-class type ("throwable error value") | partial — names the *value*, not a hierarchy |
| Values and Types | 792 | "`0.0 / 0.0` raises [a runtime error]" | **N** (code: bare `RuntimeError`, `Arithmetic.cs:172`) |
| Values and Types | 824 | "every operand mismatch **raises a `RuntimeError`**" | **names the BARE base explicitly** (code agrees: `RuntimeOps.cs:161`) |
| Values and Types | 844 | "raises a `RuntimeError`" | **names the BARE base** (code: `RuntimeOps.cs`) |
| Bindings and Scope | 905 | "Assignment to an undeclared name produces a runtime error" | **N** (code: bare, `Variables.cs:44`) |
| Bindings and Scope | 915 | "Assigning to a `const` binding produces a runtime error" | **N** (code: bare, `Variables.cs:77`) |
| Bindings and Scope | 1034,1071,1080,1115,1119,1156 | "throws `ReadOnlyError`" (frozen/readonly) | **Y — `ReadOnlyError`** (code agrees: `StashDictionary.cs:38` etc.) |
| Expressions | 1198 | "[indexing a non-indexable] produces a runtime error" | **N** (code: bare) |
| Expressions | 1280 | "Calling a non-callable value produces a runtime error" | **N** (code: bare, `Functions`) |
| Expressions | 1313 | "Invalid operand types produce a runtime error" | **N** (code: bare, `RuntimeOps.cs`) |
| Expressions | 1360 | "[index out of bounds] … runtime error" | **N** (code: bare, `Collections.cs:38`) — NOT `IndexError` |
| Expressions | 1379 | "`in` with an unsupported right operand produces a runtime error" | **N** (code: bare) |
| Expressions | 1392 | "Unknown type names produce a runtime error" | **N** (code: bare) |
| Expressions | 1405 | "Assignment to any non-assignable expression is a parse error or runtime error" | **N** (code: bare/parse) |
| Expressions | 1421 | "`++`/`--` on non-numeric … runtime error" | **N** (code: bare, `Arithmetic.cs:275`) |
| Expressions | 1437 | "[missing field] evaluation produces a runtime error" | **N** (code: bare, `StashInstance.cs:78`) |
| Statements and Control Flow | 1514 | "`for in` with a non-iterable value produces a runtime error" | **N** (code: bare) |
| Statements and Control Flow | 1525 | "Returning outside a function produces a runtime error" | **N** |
| Statements and Control Flow | 1563 | "Failure to acquire or release the lock produces a runtime error" | **N** (`LockError` exists but spec does not name it here) |
| Statements and Control Flow | 1582 | "[elevation denied] evaluation produces a runtime error" | **N** (code: bare, `ControlFlow.cs:119`) |
| Functions, Closures, and Async | 1703-1873 | many named types: `TypeError`,`ValueError`,`IOError`,`StateError`,`CancellationError`,`TimeoutError`; `throw "string"` → `RuntimeError("string")` (L1710) | **Y** (this region sealed by `language-standard-async`) |
| Aggregate Types and Members | 2149 | "Missing required fields or unknown fields produce a runtime error" | **N** (code: bare, `TypeOps.cs:355`) |
| Aggregate Types and Members | 2218 | "Ambiguous extension or UFCS matches produce a runtime error or static diagnostic" | **N** |
| Namespace Members | 2347,2375 | "raises `ReadOnlyError`" / "raises a [ReadOnlyError]" | **Y — `ReadOnlyError`** |
| Namespace Members | 2379 | "`env.cwd` may throw `IOError`" | **Y — `IOError`** |
| Shell Integration | 2432,2438,2450,2462 | strict command "produces a runtime error … expose command, exit code, stdout, stderr" | partial — describes `CommandError`'s fields but does **not name the type** (code: `CommandError`, `CommandError.cs`) |
| Errors and Cleanup | 2555-2576 | "Stash errors are values … built-in error types are specified in the [Standard Library Reference]" | **delegates the entire type catalog to the generated (non-normative) doc** |
| Errors and Cleanup | 2570 | "A bare `throw;` outside a catch clause produces a runtime error" | **N** (code: bare, `ControlFlow.cs:755`) |
| Errors and Cleanup | 2635 | "If time expires, evaluation produces a timeout error" | lowercase "timeout error" — `TimeoutError` exists but the §Errors prose does not name the type |
| Runtime Behavior | 2656 | "Parse errors, runtime errors, and static diagnostics must report source location" | N — generic |
| Runtime Behavior | 2676 | "A restricted side effect must produce a runtime error or documented host diagnostic" | N — generic |

**Direction-3 verdict: thesis CONFIRMED.** Outside the already-sealed §Async region and the `ReadOnlyError`
clauses, essentially every error claim in the spec is the **unnamed** "produces a runtime error." The spec
even names the **bare base `RuntimeError`** as the canonical thrown type for operand mismatch (L824, L844) —
the one place spec and the bare-base reality *agree by name*.

---

## 4. The Situation → Type Master Map (Axis G)

For each failure: the type actually thrown (code), a citation, and whether the spec specifies a *named*
type. "bare base" = `throw new RuntimeError(...)` → `.type == "RuntimeError"`.

| Situation | Type thrown (code) | Citation | Spec'd? (named type) |
| --------- | ------------------ | -------- | -------------------- |
| Undefined variable (read) | **bare base** | `VirtualMachine.Variables.cs:44,53` | §Bindings L905 — unnamed |
| Assign to undeclared name | **bare base** | (resolver/`Variables.cs`) | §Bindings L905 — unnamed |
| Assign to `const` binding | **bare base** | `VirtualMachine.Variables.cs:77,90` | §Bindings L915 — unnamed (NOTE: `ReadOnlyError` is **NOT** used here) |
| Mutate frozen value / readonly namespace member | **`ReadOnlyError`** | `StashDictionary.cs:38`; `VirtualMachine.TypeOps.cs:497`; `StashInstance.cs:93` | §Bindings L1034, §Namespace L2347 — **named** |
| Type / operand mismatch (arithmetic) | **bare base** | `RuntimeOps.cs:161,243,268`; `Arithmetic.cs:275` | §Values L824 names **bare `RuntimeError`** |
| Type mismatch (stdlib argument) | **`TypeError`** | `Stash.Stdlib/SvArgs.cs:22,30,38` (169 sites) | §Async L1706 — named; elsewhere unnamed |
| Index out of bounds (array/string, VM) | **bare base** | `VirtualMachine.Collections.cs:26,38,66` | §Expressions L1360 — unnamed (NOT `IndexError`) |
| Index out of bounds (stdlib `str.substring`) | **`IndexError`** | `StrBuiltIns.cs:204,209` | unnamed |
| Index out of bounds (stdlib `buf`) | **bare base** | `BufBuiltIns.cs:183,893` | unnamed (inconsistent with `str`) |
| Non-integer index | **bare base** | `VirtualMachine.Collections.cs:23,33` | §Expressions — unnamed |
| Dict key missing (read) | returns `null` (**no throw**) | `Stash.Core/Runtime/Types/StashDictionary.cs:48` (`Get` returns `StashValue.Null` on miss); confirmed via probe `d["x"]` → `null` | §Aggregate — unspecified (cf. Python `KeyError`; **decision point**) |
| Dict key is `null` | **bare base** | `StashDictionary.cs:169,200`; `Collections.cs:46` | unspecified |
| Division by zero (int & literal float) | **bare base** | `Arithmetic.cs:165,172`; `RuntimeOps.cs:275` | §Values L792 — unnamed |
| Arithmetic NaN (`Inf - Inf`) | no throw (produces `NaN`) | §Values L792 | §Values L792 — specified as NaN |
| Assign wrong type to typed-array element | **bare base** | `StashIntArray.cs:57`; `StashStringArray.cs:65` | unspecified |
| Byte-array element out of `[0,255]` | **bare base** | `StashByteArray.cs:164,167` | unspecified |
| Missing required / unknown struct field | **bare base** | `StashInstance.cs:78`; `TypeOps.cs:355` | §Aggregate L2149 — unnamed |
| Access field on non-struct | **bare base** | `VirtualMachine.TypeOps.cs:488,506` | §Expressions L1437 — unnamed |
| Call non-callable | **bare base** | (`Functions`) | §Expressions L1280 — unnamed |
| Import: module not found | **bare base** | `VirtualMachine.Modules.cs:102,272` | §Modules — unnamed |
| Import: circular / cycle | **bare base** | `VirtualMachine.Modules.cs:79` | §Modules L320 — unnamed |
| Import: non-string path | **bare base** | `VirtualMachine.Modules.cs:330,355` | unspecified |
| Import: member not exported | **bare base** | `VirtualMachine.Modules.cs:340` | unspecified |
| Import: package no entry point | **bare base** | `VirtualMachine.Modules.cs:316` | unspecified |
| Command / subprocess non-zero (strict `$!`) | **`CommandError`** | `CommandError.cs` | §Shell L2432-2438 — fields described, **type not named** |
| Timeout (`timeout` block, stdlib) | **`TimeoutError`** | `Stash.Stdlib/BuiltIns/NetSocketImpl.cs:228` (25 sites) | §Async L1842 named; §Errors L2635 "timeout error" unnamed |
| Timeout (`http.download`) | **bare base** | `HttpBuiltIns.cs:338` | unspecified (inconsistent with `TimeoutError`) |
| Cancellation (Ctrl-C / `task.cancel`) | **`CancellationError`** | `Stash.Core/Runtime/Types/StashFuture.cs:72` | §Async L1711, L1969 — named |
| Assertion failure | **`AssertionError`** (unregistered) | `Stash.Stdlib/BuiltIns/AssertBuiltIns.cs` (12 sites) | §Equality L730 — **named, but type is unregistered** |
| I/O failure (fs/net) | **`IOError`** (and bare base in some stdlib) | `Stash.Stdlib/BuiltIns/SysBuiltIns.cs:128` (134 sites) | §Async L1706, §Namespace L2379 — named |
| Value out of domain (stdlib) | **`ValueError`** | stdlib `Args` (109 sites) | §Errors L2566 example — named |
| Value out of domain (VM, e.g. shift count) | **bare base** | `Arithmetic.cs:334,351` | unspecified |
| Not supported / platform | **`NotSupportedError`** | `Stash.Core/Runtime/Types/StashStreamingProcess.cs:387` (6 sites) | **NOT spec'd by name** |
| Lock acquisition failure | **`LockError`** | `Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs:184` (2 sites) | §Statements L1563 — unnamed |
| Elevate in embedded mode | **bare base** | `VirtualMachine.ControlFlow.cs:119` | §Statements L1582 — unnamed |
| CLI parse failure (9 variants) | **`Cli*`** family | `Stash.Stdlib/BuiltIns/CliBuiltIns.Parse.cs:290,365,388,489,499,1014,1278`; `CliBuiltIns.cs:377`; `CliBuiltIns.Validation.cs:58` | **NOT spec'd** |
| Invalid state (async, sftp, test ctx) | **`StateError`** (async) / **bare base** (sftp, test) | `StashStreamingProcess.cs:419` (`StateError`); `SftpBuiltIns.cs:495`, `TestBuiltIns.cs:194` (bare) | §Async L1797 named; others unspecified |
| Host CLR exception escapes delegate | **`HostError`** | `Stash.Hosting/Internal/InvokeHostDelegate.cs:81` (9 sites, `Stash.Hosting` only) | **NOT spec'd** |
| `throw "string"` (non-error value) | wraps to `RuntimeError("string")` | §Async L1710 | §Async L1710 — specified |
| `retry` exhausted | rethrows last error (no dedicated type) | `ControlFlow.cs:401` | §Errors L2622 — "produces the last error" |
| Bare `throw;` outside catch | **bare base** | `VirtualMachine.ControlFlow.cs:755` | §Errors L2570 — unnamed |

**Note on `RetryExhaustedError` / generic `RetryExhausted`:** the task's axis-F list anticipated a
dedicated retry-exhaustion type; **none exists** — `retry` re-raises the *last* error (whatever type it was),
matching §Errors L2622 ("produces the last error"). Worth a sealing decision (is "rethrow last" the law, or
should there be a `RetryExhaustedError`?).

---

## 5. Divergence Ledger (the heart)

Each entry: a two-world framing. **A** = spec becomes law (code changes to conform). **B** = code becomes
law (spec changes to conform). Decision-neutral.

### D1 — §Errors delegates the entire type catalog to a non-normative generated doc
- **Spec:** "Stash errors are values. The standard built-in error types **are specified in the Standard
  Library Reference**." (§Errors L2555-2556). §Errors does **not** define a root type, enumerate the
  types, or state catch-by-name rules. The Standard Library Reference is **generated from code**
  (`docs/Stash — Standard Library Reference.md` is non-normative, derived from `StdlibDefinitions` +
  `BuiltInErrorRegistry`).
- **Code:** the type set is whatever has `[StashError]` (23 types) plus three unregistered
  (`RuntimeError`/`AssertionError`/`UserRuntimeError`). The "law" is the generated table.
- **Law A (spec wins):** §Errors must itself normatively enumerate the type taxonomy, the root type, and
  catch semantics — the generated doc becomes a derived view, not the source of truth. Consequence: a
  large normative section to author; code must conform to whatever the spec enumerates (e.g. registering
  `AssertionError`).
- **Law B (code wins):** ratify "the error catalog is defined by the registry and surfaced in the
  generated reference; the spec points to it." Consequence: the spec stays thin and the registry is the
  law — but then unregistered types (`RuntimeError`, `AssertionError`) are *outside* the law and need an
  explicit status.

### D2 — Is there a single user-catchable root type? (Axis A crux)
- **Spec:** §Values L600 lists `Error` as a first-class type ("throwable error value"); §Errors L2580 uses
  `result is Error`; §Errors L2596 uses untyped `catch (e)`. Implies a root named `Error`.
- **Code:** the C# root is `RuntimeError` (unregistered). The **Stash-facing** root is the string
  `"Error"` — `ErrorTypeRegistry.BaseTypeName = "Error"` (`ErrorTypeRegistry.cs:11`), and
  `Matches(_, "Error")` is the catch-all. But there is **no registered type named `Error`**; `Error` is a
  pure *matching sentinel*, while the actual thrown C# base surfaces as `.type == "RuntimeError"`.
- **Law A (spec wins):** ratify `Error` as the normative root, with `catch (Error e)` / `is Error` as the
  catch-all. Consequence: the name `RuntimeError` should arguably never surface as a `.type` to users
  (rename/hide the bare base), since the spec's root is `Error`, not `RuntimeError`.
- **Law B (code wins):** ratify the two-name reality — `Error` is the catch-all sentinel; `RuntimeError`
  is the concrete `.type` of any untyped throw. Consequence: users can legitimately see and catch
  `"RuntimeError"`, and SA0163 (which discourages it) is in tension with that.

### D3 — Assign-to-`const` throws the bare base, not `ReadOnlyError`
- **Spec:** "Assigning to a `const` binding produces a runtime error." (§Bindings L915) — unnamed. (Note:
  §Bindings *does* name `ReadOnlyError` for **frozen/namespace** writes at L1034 — a different situation.)
- **Code:** `throw new RuntimeError($"Cannot assign to constant '{name}'.", ...)` —
  **bare base** (`VirtualMachine.Variables.cs:77,90`). `ReadOnlyError` is reserved for frozen-collection /
  read-only-namespace-member mutation (`StashDictionary.cs:38`, `TypeOps.cs:497`).
- **Law A (spec wins):** if the seal decides const-assignment should be a *named* type, `ReadOnlyError` is
  the natural fit (read-only binding). Consequence: change the throw site; `catch (ReadOnlyError e)`
  would then catch both const-reassignment and frozen-mutation.
- **Law B (code wins):** ratify that `const`-reassignment is an untyped runtime error distinct from
  `ReadOnlyError` (which is specifically about *frozen values / namespace members*, not *bindings*).
  Consequence: the spec stays unnamed here, and the two situations remain deliberately different types.
- **NOTE:** the task hint said "assign-to-const throws bare unregistered `RuntimeError`" — **confirmed**;
  but it is *not* a `ReadOnlyError` gap, because `ReadOnlyError` exists for a related-but-distinct purpose.

### D4 — Index-out-of-bounds throws the bare base in the VM, but `IndexError` exists
- **Spec:** §Expressions L1360 "[out of bounds] … produces a runtime error" — unnamed.
- **Code:** the VM throws **bare base** (`VirtualMachine.Collections.cs:26,38,66`). The registered
  `IndexError` is used in only **6** places, all stdlib (`StrBuiltIns.cs:204`, `BufBuiltIns`,
  `ArrBuiltIns.cs:96`) — and even within stdlib, `buf` OOB throws bare base (`BufBuiltIns.cs:183`).
- **Law A (spec wins, if spec names `IndexError`):** the VM index path must throw `IndexError`.
  Consequence: ~32 bound-check sites change; `catch (IndexError e)` becomes reliable for all indexing.
- **Law B (code wins):** ratify "core indexing throws untyped runtime error; `IndexError` is stdlib-only."
  Consequence: users cannot reliably `catch (IndexError e)` an array index — they must catch the base.
  The `str`-vs-`buf` inconsistency would still need a separate ruling.

### D5 — Operand type-mismatch: spec names the BARE base; stdlib uses `TypeError`
- **Spec:** §Values L824 "every operand mismatch **raises a `RuntimeError`**"; L844 "raises a
  `RuntimeError`." This is the **one place the spec names the bare base by name.**
- **Code:** VM operand mismatch throws **bare `RuntimeError`** (`RuntimeOps.cs:161,243,268`) — *agrees with
  the spec*. But stdlib argument type-mismatch throws **`TypeError`** (`SvArgs.cs:22`, 169 sites).
- **Law A (spec wins):** ratify that operand mismatch is intentionally the base `RuntimeError`, NOT
  `TypeError`. Consequence: the spec's existing wording stands; but then `TypeError` is "for stdlib
  arguments only," and the spec should say so — and possibly the bare `RuntimeError` mention should be
  reconciled with the `Error`/root question in D2.
- **Law B (code unifies upward):** decide operand mismatch *should* be `TypeError` (consistency with
  stdlib). Consequence: change ~62 VM sites AND change the spec L824/L844 (which currently names the base).
  This is the rare divergence where the *spec* points at the bare base and the *fix* would re-type it.

### D6 — `AssertionError` is thrown and spec-named, but unregistered
- **Spec:** §Equality L730 "`assert.equal(1, 1.0)` raises `AssertionError`" — names the type.
- **Code:** `AssertionError` (`Stash.Core/Runtime/AssertionError.cs:9`) is thrown 12× by `AssertBuiltIns`
  but has **no `[StashError]`** — so it is not in `BuiltInErrorRegistry`, not in the generated reference,
  and `BuiltInClrType` is null for it (catchable only by name-string-match or base).
- **Law A (spec wins):** register `AssertionError` with `[StashError]` (and surface `Expected`/`Actual` as
  Properties). Consequence: it joins the documented catalog; `catch (AssertionError e)` gets the CLR fast
  path; the generated reference lists it.
- **Law B (code wins):** ratify `AssertionError` as a deliberately *test-only, unregistered* type.
  Consequence: it stays absent from the public catalog and the spec mention at L730 is the only place it
  is "public" — an oddity to seal explicitly.

### D7 — `CommandError` / `TimeoutError`: fields/behavior described but type unnamed in §Errors/§Shell
- **Spec:** §Shell L2438 "The runtime error must expose command, exit code, stdout, and stderr" (describes
  `CommandError` without naming it). §Errors L2635 "produces a timeout error" (lowercase, unnamed). The
  type names appear only in an *example* (`catch (CommandError e)`, L2592) and in the sealed §Async region
  (`TimeoutError` L1842).
- **Code:** `CommandError` (`CommandError.cs`) with exactly those four props; `TimeoutError` for timeout.
- **Law A (spec wins):** §Shell/§Errors must name `CommandError`/`TimeoutError` normatively (not just in an
  example). Consequence: prose changes; types are pinned.
- **Law B (code wins):** ratify "the example *is* the spec; prose describes fields generically."
  Consequence: weaker guarantee — an implementation could throw a differently-named type with the same
  fields and still conform.

### D8 — Catch type-names are not validated; unknown names silently never match
- **Spec:** §Errors L2601 "A typed catch matches errors of that type." Silent on what happens for an
  unknown/misspelled type name in a catch clause.
- **Code:** `GetTypeNames` stores the raw string (`Compiler.Exceptions.cs:155`); `ExecuteCatchMatch`
  (`ControlFlow.cs:713-732`) does string/CLR matching. A misspelled or non-existent type name **compiles
  and simply never matches** (falls through to the next clause / rethrow). Analyzer SA0169 flags some
  unreachable catches, but a typo like `catch (TpyeError e)` is not a hard error.
- **Law A (spec wins / stricter):** decide that an unknown catch type-name is itself an error (compile-time
  diagnostic or runtime). Consequence: tighter; needs the compiler to know the full registered set + user
  types.
- **Law B (code wins):** ratify "catch matching is by name; unknown names are simply non-matching."
  Consequence: forgiving but typo-prone; the spec should say unknown names never match (currently
  unspecified).

### D9 — `NotSupportedError`, `AliasError`, runtime `ParseError`, `HostError`, all 9 `Cli*` — spec-silent
- **Spec:** names none of these 13 registered types.
- **Code:** all registered, documented in the generated reference, thrown in stdlib/cli/host.
- **Law A (spec wins):** §Errors enumerates the full taxonomy including these. Consequence: large catalog
  in the spec; future additions become spec changes.
- **Law B (code wins):** ratify "stdlib/cli/host error types live in the generated reference, not the core
  language spec." Consequence: the core spec covers only language-level errors; namespace error types are
  library surface (consistent with D1-Law-B).

### D10 — Hierarchy shape: flat, but matching is exact-string (no `is`-subtype tree)
- **Spec:** does not describe a hierarchy. Examples imply only `Error` (root) → concrete types.
- **Code:** every registered type derives directly from `RuntimeError` in C#, **but** catch/`is` matching
  is **flat exact-string** (`ErrorTypeRegistry.Matches`: base `"Error"` OR exact equality) — there is **no
  grouping** like Python's `LookupError → IndexError/KeyError`. C# inheritance (e.g. `AssertionError :
  RuntimeError`) is **not** reflected in Stash-level catch matching beyond the `Error` catch-all.
- **Law A (introduce a tree):** seal a grouped hierarchy (e.g. a `LookupError` parent for
  `IndexError`/key-missing). Consequence: `Matches` must walk a subtype graph; new intermediate types.
- **Law B (stay flat):** ratify the flat model — only `Error` (catch-all) and concrete leaves; no
  intermediate grouping. Consequence: users catch either the exact type or `Error`; no `catch (LookupError)`.

### D11 — Payload/fields are inconsistent across types (Axis D)
- **Spec:** §Errors L2558 "An error value must expose a message. Caught errors also expose `.type` and
  `.stack` where supported." (note the hedge "where supported"). The generated reference (L171) states all
  caught errors expose `.message`/`.type`/`.stack` plus type-specific fields.
- **Code:** `StashError` always exposes `.message`/`.type`/`.stack`/`.suppressed`
  (`StashError.cs:113-150`); type-specific props come from `GetProperties()` overrides (`CommandError`,
  `LockError`, `AliasError`, `CliSchemaError`, etc.). The base `RuntimeError` and most VM-thrown errors
  carry **no** extra props. There is **no `.cause`/error-chaining field** — only `.suppressed` (deferred-
  cleanup errors), which is a different concept from a cause-chain.
- **Law A (spec wins, stricter):** seal the exact field contract (always-present set + per-type set) and
  whether `.stack` is guaranteed or "where supported." Decide if a `.cause` chain exists.
- **Law B (code wins):** ratify the current set (`message`/`type`/`stack`/`suppressed` + per-type props,
  no cause-chain). Consequence: "where supported" hedge becomes "always present"; no error-chaining is law.

### D12 — `.suppressed` (deferred-cleanup errors) is observable but unspecified in §Errors
- **Spec:** §Errors §Defer L2649 says deferred actions "run on normal return and on error unwinding" but
  says **nothing** about what happens if a deferred action **itself throws** during unwinding.
- **Code:** errors from deferred cleanup during propagation are collected into
  `RuntimeError.SuppressedErrors` / `StashError.Suppressed` and exposed as `.suppressed`
  (`RuntimeError.cs:52`, `StashError.cs:131-140`).
- **Law A (spec wins):** §Errors must specify suppressed-error semantics (collected, attached to the
  primary error, observable via `.suppressed`, ordering). Consequence: new normative clause.
- **Law B (code wins):** ratify the existing `.suppressed` behavior verbatim. Consequence: lifts
  implementation detail to law; must pin ordering and whether the *primary* error or the cleanup error wins.

### D13 — `retry` exhaustion has no dedicated type (no `RetryExhaustedError`)
- **Spec:** §Errors L2622 "If all attempts fail, evaluation produces the last error."
- **Code:** matches — `retry` re-raises the last caught error (`ControlFlow.cs:401` rewraps as
  `UserRuntimeError` preserving type/message). No `RetryExhaustedError` type exists.
- **Law A (introduce a type):** seal a `RetryExhaustedError` wrapping the last error + attempt count.
  Consequence: new type; `catch` semantics change (callers catch the wrapper, not the inner type).
- **Law B (code wins):** ratify "retry produces the last error, untyped-wrapped." **Aligned today** — this
  is a "confirm the existing law" entry, not a true divergence.

### D14 — `throw "string"` / non-error throw wrapping
- **Spec:** §Async L1710 "`throw \"string\"` inside a task wraps to `RuntimeError(\"string\")`." §Errors
  §Throw L2563 "`throw expr;` evaluates `expr` and raises it as an error" — does not say what happens when
  `expr` is a non-error value (string/number) in **non-async** code.
- **Code:** throwing a non-error value wraps it (the async clause documents one case; the general case is
  the same mechanism).
- **Law A (spec wins):** §Errors §Throw must specify non-error-value throw wrapping generally (not only in
  §Async). Consequence: lift the async clause to the general throw rule.
- **Law B (code wins):** ratify current behavior and generalize the spec to match. **Mostly aligned**;
  the gap is that the general §Errors §Throw clause is silent where §Async is specific.

---

## 6. Per-Axis Summary (A–F)

**A. Root type & catchability.** De-facto: the C# root is the **unregistered** `RuntimeError`
(`RuntimeError.cs:26`); the Stash-facing root is the **string sentinel `"Error"`**
(`ErrorTypeRegistry.cs:11`) used by `catch (Error e)` / `is Error` as the catch-all. There is no
registered type literally named `Error`. A bare untyped throw surfaces as `.type == "RuntimeError"` and is
catchable by `catch (RuntimeError e)` (string-match) — but analyzer **SA0163** discourages it.
**Open question:** is `Error` the normative root (and `RuntimeError` an internal name that should never
surface), or are both public (catch-all `Error` + concrete `RuntimeError`)? (D2.)

**B. Hierarchy shape.** De-facto: **flat**. 23 registered leaves all derive from `RuntimeError` in C#, but
Stash-level matching is **exact-string + a single `Error` catch-all** (`ErrorTypeRegistry.Matches`,
`ErrorTypeRegistry.cs:24-31`) — no intermediate grouping, C# inheritance is invisible to `catch`.
**Open question:** stay flat, or introduce grouping parents (Python-style `LookupError`)? (D10.)

**C. Naming convention.** De-facto: `*Error` suffix is consistent for the 14 core/runtime types. The
`Cli*` family is **mixed** — **7 of 9 break the convention** (`CliAmbiguousOption`, `CliInvalidValue`,
`CliMissingRequired`, `CliMissingValue`, `CliUnexpectedPositional`, `CliUnknownCommand`, `CliUnknownOption`
have **no `Error` suffix**), while only `CliSchemaError` and `CliValidationFailed` carry it. The hosting DTO
is `Stash.Hosting.StashError` (suffix OK but a *record*, not a runtime type). **Open question:** seal the
`*Error` convention and rule on the `Cli*` outliers (rename vs. exempt as a sub-family). (Inventory §1.)

**D. Payload / fields.** De-facto: every caught error exposes `.message`, `.type`, `.stack`, `.suppressed`
(`StashError.cs:113-150`); type-specific props via `GetProperties()` overrides (only ~6 types carry extra
props). **No `.cause`/error-chaining.** Spec hedges "`.type` and `.stack` where supported" (§Errors L2558)
while the generated reference says they are always present (L171). **Open question:** pin the always-present
set, whether `.stack` is guaranteed, and whether a cause-chain exists. (D11, D12.)

**E. Built-in vs user-raised.** De-facto: user code raises via `throw expr;` (§Errors L2563). Two raise
forms: (1) `throw SomeError { message: ... }` struct-literal of a *built-in* type, and (2)
`throw { type: "MyError", message: ... }` dict literal → **`UserRuntimeError`** carrying an arbitrary
runtime string name (`UserRuntimeError.cs:10,26`), catchable by that name. Throwing a non-error value wraps
it (§Async L1710). **Users cannot define a new C# error subclass**, but the dict-`throw` path gives
arbitrary user type *names* with full catch participation. **Open question:** is the dict-`throw` /
`UserRuntimeError` mechanism the sanctioned user-error story, and should `throw TypeName {…}` for a
user-declared struct be elevated to a first-class typed-throw? (analyzer SA0860 already hints at a future
"typed form"). (D14, §1.)

**F. Catch / recovery semantics.** De-facto: `try`/`catch`/`finally` (§Errors L2585) with source-order
clause testing; typed catch matches via CLR-identity fast-path (built-ins) then exact-string fallback
(`ControlFlow.cs:713-732`); untyped `catch (e)` and `catch (Error e)` are catch-all. `throw;` rethrows the
original (preserving span/stack, `ControlFlow.cs:738-749`). `finally` runs on every exit path. `defer` runs
LIFO on return and unwinding (§Errors L2649). `retry` re-raises the last error (no `RetryExhaustedError`,
D13); `timeout` → `TimeoutError`; cancellation → `CancellationError`; assertion → `AssertionError`
(unregistered, D6). Uncaught errors propagate to the top level (host surfaces them as
`Stash.Hosting.StashError`). **Catch type-names are not validated** — unknown names silently never match
(D8). **Open question:** validate catch names? specify uncaught-at-top-level behavior (exit code, stderr
format)? specify suppressed-during-unwind semantics (D12)?

---

## 7. Scope Flag (do NOT decide)

**Bare-base (`throw new RuntimeError(...)`) throw-site total: 351** (whole tree minus `Stash.Tests/**`;
`Stash.Registry*` contributes 0). Distribution: `Stash.Stdlib` 149, `Stash.Bytecode` 144, `Stash.Core` 52,
`Stash.Cli` 5, `Stash.Hosting` 1.

Rough categories (indicative keyword buckets, §2b): type/operand mismatch ~62 · index/bounds ~32 ·
null/empty/value-domain ~24 · undefined/not-found/no-export ~15 · import/module ~14 · struct/field ~12 ·
division-by-zero ~9 · const/non-assignable-assign ~9 · plus a large remainder of stdlib argument-arity and
miscellaneous guards.

Plus **3 unregistered types** in the catch hierarchy: the base `RuntimeError`, `AssertionError` (12 throw
sites, spec-named at §Equality L730), and `UserRuntimeError` (the user-error vehicle).

This is reported, not triaged: the architect/user decides per-unit whether to re-type these sites (and
which buckets) inside `language-standard-errors` vs. backlog them. No recommendation is made here.

---

## Appendix — Methodology / reproduction

- Registered set: `grep -rl "^\[StashError" Stash.Core/Runtime/Errors/*.cs` (23, excluding the attribute
  definition).
- Bare-base census: `grep -rn "throw new RuntimeError(" --include="*.cs" <all projects> | grep -v "/bin/\|/obj/"`
  excluding `Stash.Tests/**` (351). `UserRuntimeError`: 8 (all VM-internal rewraps).
- Catch mechanic: `ErrorTypeRegistry.Matches` (`Stash.Core/Runtime/ErrorTypeRegistry.cs:24`) +
  `ExecuteCatchMatch` (`Stash.Bytecode/VM/VirtualMachine.ControlFlow.cs:695`).
- `.type` resolution: `NameOf` emitted by `Stash.Stdlib.Generators/StashErrorRegistryGenerator.cs:255`.
- Spec phrase census: `grep -nE "runtime error|throws?|raises?|an error|errors? are"` against
  `docs/Stash — Language Specification.md` (97 hits; load-bearing ones tabulated in §3).
