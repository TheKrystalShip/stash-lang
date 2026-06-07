# Error Models Across Languages — A Comparative Reference for Stash's Error Taxonomy

> **Purpose.** Reference material for the `language-standard-errors` spec unit (sealing Stash's
> built-in error-type taxonomy as normative law). This surveys how other languages — especially
> dynamic/scripting languages — model runtime errors, organized so each cell lines up
> axis-for-axis with a parallel spec-vs-impl analysis of Stash itself. It is a set of **reference
> points to compare against, not a prescription**. The Stash design decisions are reserved for the
> human design session.
>
> **Stash's current shape (the thing being compared).** ~25 built-in error types
> (`TypeError`, `ValueError`, `IndexError`, `IOError`, `ParseError`, `StateError`, `TimeoutError`,
> `NotSupportedError`, `ReadOnlyError`, `CancellationError`, `CommandError`, `HostError`, the CLI
> family, …) deriving from a single base `RuntimeError`; a roughly **flat** hierarchy; uniform
> `*Error` naming; typed `try`/`catch` matching; per-type structured payload declared via
> `[StashError(Properties=…, PropertyTypes=…, Description=…)]`. Source of truth:
> `Stash.Core/Runtime/Errors/` and `BuiltInErrorRegistry`.

## The shared decision-axis spine (A–G)

These seven axes are the spine; every matrix column and every deep-dive subsection below uses the
**same labels verbatim** so this document aligns mechanically with the Stash spec-vs-impl analysis.

- **A. Root type & catchability** — single base all errors derive from? directly catchable? a
  meaningful split (system-vs-ordinary)?
- **B. Hierarchy shape** — flat vs tree; depth; grouping that buys catch-supertype power.
- **C. Naming convention** — `*Error` vs `*Exception` vs `*Err`; consistency.
- **D. Payload / fields** — structured data a caught error carries; how the cause chain is
  represented.
- **E. Built-in vs user-raised / user-defined** — how user code raises; whether users subclass
  built-ins; whether arbitrary values can be raised.
- **F. Catch / recovery semantics** — try/except/rescue/catch/pcall; how a handler matches a type;
  finally/ensure; re-raise; uncaught-at-top-level behavior; retry.
- **G. The situation→type map** — concrete type/mechanism for each canonical failure (its own
  matrix in §2).

---

## 1. Master comparison matrix (axes A–F)

Rows = languages. Cells are compact; citation footnotes `[n]` resolve in §5. Core scripting
languages first, then the contrast trio.

| Language | A. Root & catchability | B. Hierarchy shape | C. Naming | D. Payload / cause chain | E. Raise / user-define | F. Catch & match |
|---|---|---|---|---|---|---|
| **Python** | `BaseException` root; user code catches **`Exception`** (subtree), *not* `BaseException` — the latter also holds `SystemExit`/`KeyboardInterrupt`/`GeneratorExit` which should propagate.[1] | **Deep tree.** `Exception → ArithmeticError → ZeroDivisionError`; `Exception → LookupError → {IndexError, KeyError}`; `Exception → OSError → {FileNotFoundError, PermissionError, …}`. Grouping is load-bearing.[1] | Uniform `*Error` (plus a separate `Warning` subtree). | `args`; `__traceback__`; **`__cause__`** (explicit, `raise X from Y`), **`__context__`** (implicit, set when raising during handling); `__notes__`/`add_note` (3.11+).[1] | `raise Expr`; subclass `Exception` or any subclass to define new types; **must be a BaseException instance** (cannot raise arbitrary values).[1] | `try/except T as e/else/finally`; match by **class (isa)** — an `except T` clause catches `T` and all subclasses; tuple `except (A, B)`; bare `raise` re-raises; uncaught → traceback to stderr, exit. No retry.[1] |
| **Ruby** | `Exception` root, but **`rescue` with no class defaults to `StandardError`**, *not* `Exception`. System-level (`SystemExit`, `NoMemoryError`, `SignalException`, `ScriptError`, `SystemStackError`) sit **outside** `StandardError` so a bare `rescue` won't swallow them.[2] | **Tree, two-tier.** `Exception → StandardError → {TypeError, ArgumentError, IndexError→KeyError, RangeError, ZeroDivisionError, IOError, RuntimeError, NameError→NoMethodError, …}`. The split-from-StandardError *is* the design feature.[2] | Uniform `*Error`. | `message`, `backtrace`, **`cause`** (`Exception#cause` — auto-set to the in-flight exception when you `raise` inside a `rescue`).[2] | `raise` (bare = re-raise current / `RuntimeError`), `raise Class, msg`; define by subclassing (usually `StandardError`); the raised thing must be an `Exception` (or a class/instance of one). | `begin/rescue T => e/else/ensure/end`; match by **class (isa)**; `retry` keyword **re-runs the begin block**; `raise` with no args re-raises. Uncaught → message+backtrace, nonzero exit.[2] |
| **JavaScript / Node** | Single `Error`. **No system/ordinary split** — every standard error is just `Error` or a direct subtype. `Error` is directly catchable (one catch binding catches everything). | **Flat.** `RangeError`, `ReferenceError`, `SyntaxError`, `TypeError`, `URIError`, `EvalError`, `AggregateError` **all directly extend `Error`** — no intermediate grouping nodes.[3] | Uniform `*Error`. | `message`, `name`, **`cause`** (`new Error(msg, { cause })`, read via `err.cause`, ES2022), `stack` (de-facto), `AggregateError.errors` (array).[3] | `throw <anyValue>` — **you can throw any value** (`throw "str"`, `throw 42`), not only `Error`s; define types by `class X extends Error`.[3] | `try/catch(e)/finally` with a **single untyped binding** — no per-type catch clause; discriminate manually via **`e instanceof T`** or **`e.name`** string. Re-throw with `throw e`. Uncaught → unhandled exception / `unhandledRejection`. No retry.[3] |
| **Lua** | **No root type, no exception classes at all.** An "error object" is **any Lua value** (string, table, number).[4] | **None** (no hierarchy to shape). | n/a — no error classes. | Whatever value you raised. Convention: a table with fields, or a plain string `"file:line: msg"`. `xpcall`'s message handler can capture a traceback (`debug.traceback`).[4] | `error(value [, level])` raises **any value**; `assert(v, msg)` raises `msg` (or a default) when `v` is falsy. "Define a type" = adopt a table convention; there's no language support.[4] | **`pcall(f, …)` → `(ok, result|errval)`**; `xpcall(f, handler, …)` adds a message handler run before the stack unwinds. **No typed catch** — you `pcall`, then inspect the returned value yourself (check `type()`, table fields, or string match). Uncaught → propagates to host; standalone `lua` prints it. No finally (emulate via `pcall` + manual cleanup).[4] |
| **Perl** | **No built-in hierarchy.** An exception is whatever you `die` with — a **string** or an **arbitrary blessed object/reference**. Lands in **`$@`**.[5] | **None** in core. Class hierarchies exist only via CPAN (`Exception::Class`, `Throwable`). | n/a in core (CPAN modules vary). | If a string: the message (Perl appends `" at FILE line N."` unless it ends in `\n`). If an object: whatever fields you blessed in. No standard cause chain in core.[5] | `die LIST` (string or ref/object); `croak`/`confess` (Carp) for caller-context messages. Define types = bless your own class. Arbitrary values allowed (it's just a scalar).[5] | **`eval { … }; if ($@) {…}`** (or `Try::Tiny`'s `try/catch`, or native `try/catch` in newer Perl). Match by **inspecting `$@`**: `ref($@)`, `blessed($@)->isa(...)`, or string regex. No type-directed dispatch in core `eval`. Uncaught → prints `$@` to stderr, exits nonzero.[5] |
| **PHP** | **`Throwable` interface root**, with a deliberate **two-branch split**: **`Error`** (engine/internal, formerly fatal) and **`Exception`** (userland). `catch (Throwable)` catches both; `catch (Exception)` misses `Error`s.[6] | **Tree under each branch.** `Error → {TypeError→ArgumentCountError, ValueError, ArithmeticError→DivisionByZeroError, AssertionError, UnhandledMatchError, CompileError→ParseError}`; `Exception → {RuntimeException, LogicException→{InvalidArgumentException, OutOfRangeException}, …}`.[6] | **Mixed.** Engine branch uses `*Error`; userland branch uses `*Exception`. (`OutOfBoundsException` vs `OutOfRangeException` are *different* SPL types — a known footgun.) | `getMessage()`, `getCode()`, **`getPrevious()`** (cause chain via the 3rd constructor arg `$previous`), `getTrace()`, `getFile()`, `getLine()`.[6] | `throw new T(...)`; user types subclass `Exception` (or `Error`); **only `Throwable` instances may be thrown** (no arbitrary values). | `try/catch (T $e)/finally`; match by **type**; **multi-catch `catch (A|B $e)`**; re-throw `throw $e`. Uncaught → fatal error / `set_exception_handler`. No retry.[6] |
| **Go** *(contrast)* | **No exceptions for ordinary failure.** Errors are **values**: the built-in `error` interface (`Error() string`). `panic`/`recover` exist but are reserved for *truly* exceptional/programmer-bug conditions.[7] | **Flat / open.** No hierarchy; `error` is an interface anyone implements. Sentinel values (`io.EOF`) and concrete error types coexist; "grouping" is via **wrapping**. | `errors.New`, `xxxError` types, `ErrXxx` sentinels — convention, not enforced. | Whatever your error type holds. Cause chain = **wrapping** with `fmt.Errorf("…: %w", err)`.[7] | `return …, err` (values). `panic(v)` for the exceptional path. Define = implement `error`. | **`if err != nil`** at every call site; **`errors.Is(err, target)`** (sentinel match through wrap) and **`errors.As(err, &target)`** (type match through wrap). `recover()` inside `defer` catches a `panic`. No try/catch.[7] |
| **Rust** *(contrast)* | **Two channels, by design.** `Result<T, E>` for **recoverable** errors (a returned value); **`panic!`** for **unrecoverable** ones (unwind/abort). "Rust doesn't have exceptions."[8] | **Open trait, not a tree.** `std::error::Error: Debug + Display`; any type implements it. Grouping = wrapping + `source()`. | `XxxError` enums/structs; `*Error` convention. | Your `E` type. Cause chain = **`Error::source()`** returning the lower-level error.[9] | `Err(e)` value / `panic!("msg")`. Define `E` = any type (commonly an `enum` implementing `Error`). | **`match` on `Result`**, or **`?`** to propagate an `Err` up; `unwrap()`/`expect()` convert `Err`/`None` into a `panic`. `catch_unwind` exists but is not idiomatic catch. No try/catch.[8] |
| **Elixir / Erlang** *(contrast)* | **Both models, intentionally.** Expected failures → **`{:ok, _}` / `{:error, reason}` tuples**; exceptional → `raise` an exception struct. Plus **"let it crash"**: an unhandled error kills the process, a supervisor restarts it.[10] | **Flat-ish structs.** `RuntimeError` (the default for `raise "msg"`), `ArgumentError`, `ArithmeticError`, `KeyError`, … are exception **structs**; no deep inheritance tree (structs, not classes). | `*Error` for structs. | Exception structs carry a **`:message`** field (and any custom fields); `Exception.message/1`. | `raise "msg"` (→ `RuntimeError`) / `raise T, message: …`; **`defexception`** defines a struct exception. Bang functions (`File.read!`) raise; non-bang return tuples. | **`try/rescue T -> …/catch/after`**; `rescue` matches by **struct type**; **`reraise e, __STACKTRACE__`** re-raises preserving the trace. Idiom is often *not* to rescue (let it crash). Tuple path uses `case`/`with` pattern-matching, not catch.[10] |

---

## 2. Situation → type matrix (axis G)

Rows = canonical failure situations. Cells = the concrete built-in type **or** mechanism. The
**single most decision-relevant column distinction** for Stash is **"raises vs returns a sentinel
(`nil`/`undefined`/`None`)"** — several scripting languages return a sentinel where Stash raises
`IndexError`/`KeyError`. That distinction is called out explicitly per cell.

| Situation | Python | Ruby | JS / Node | Lua | Perl | PHP | Go | Rust | Elixir |
|---|---|---|---|---|---|---|---|---|---|
| **Index out of bounds** | **raises** `IndexError`[1] | `arr[i]` → **`nil` (sentinel)**; `arr.fetch(i)` → **raises** `IndexError`[2][11] | `arr[i]` → **`undefined` (sentinel)**, no throw[3] | `t[i]` → **`nil` (sentinel)**[4] | `$a[i]` → **`undef` (sentinel)** | `$a[i]` → **`null` + Warning** (not an exception)[12] | `s[i]` → **`panic`** (runtime) | `vec[i]` → **`panic`**; `vec.get(i)` → `Option::None`[8] | `Enum.at` → `nil`; `elem/2` OOB → **raises** `ArgumentError` |
| **Missing dict / hash key** | **raises** `KeyError`; `dict.get(k)` → `None`[1] | `h[k]` → **`nil`**; `h.fetch(k)` → **raises** `KeyError`[2] | `obj[k]` → **`undefined`**[3] | `t[k]` → **`nil`**[4] | `$h{k}` → **`undef`** | `$a[k]` → **`null` + Warning**[12] | `m[k]` → **zero value** (+ `, ok` idiom); read of nil map ok, **write panics**[7] | `HashMap::get` → `Option::None`; `map[k]` indexing → **`panic`** if absent[8] | `map[k]`/`Map.get` → `nil`; `Map.fetch!` → **raises** `KeyError`[10] |
| **Type mismatch / wrong-type op** | `TypeError`[1] | `TypeError`[2] | `TypeError`[3] | error: `"attempt to perform arithmetic on a … value"` (string val)[4] | usually coerces/warns; `die`s only in strict contexts | `TypeError` (engine)[6] | compile error (static) / `panic` on bad assertion | compile error (static); bad downcast `panic` | `ArgumentError` / `FunctionClauseError` |
| **Call undefined name (variable)** | `NameError`[1][13] | `NameError`[2] | `ReferenceError`[3] | indexes to `nil`, then **error on use** (`attempt to call a nil value`)[4] | strict: compile `die`; else `undef` | (engine) `Error` / undefined-constant `Error`[6] | compile error (static) | compile error (static) | `CompileError` / `UndefinedFunctionError` |
| **Call undefined method/attr** | `AttributeError`[1][13] | `NoMethodError` (⊂ `NameError`)[2] | `TypeError` (`x.foo is not a function`)[3] | `attempt to call a nil value (field 'foo')`[4] | `Can't locate object method …` (`die`) | `Error` (undefined method)[6] | compile error (static) | compile error (static) | `UndefinedFunctionError` / `KeyError` (struct field) |
| **Division by zero — integer** | **`ZeroDivisionError`**[1] | **`ZeroDivisionError`** (`1/0`)[14] | `1/0` → **`Infinity`** (Numbers have no int/float split); **BigInt** `1n/0n` → **`RangeError`**[3][15] | **error** `"attempt to perform 'n//0'"` (int `//`)[4] | `Illegal division by zero` (`die`) | **`DivisionByZeroError`** (`intdiv`, `%`; the `/` operator throws it since PHP 8)[12] | **`panic`** (integer divide by zero)[7] | **`panic`**[8] | **`ArithmeticError`** |
| **Division by zero — float** | **`ZeroDivisionError`** (`1.0/0.0` also raises — Python is strict for floats too)[1][14] | **`Infinity`/`NaN`** (`1.0/0` → `Infinity`)[14] | **`Infinity`/`NaN`**[3] | **`inf`/`nan`**, no error (float `/`)[4] | `Illegal division by zero` (`die`) | `INF`/`NAN` via `fdiv()`; `/` throws otherwise[12] | `+Inf`/`NaN` (IEEE-754, no panic) | `inf`/`NaN` (IEEE-754, no panic) | `1.0/0.0` → **`ArithmeticError`** |
| **Integer overflow** | **none** — `int` is arbitrary-precision; auto-grows[16] | **none** — promotes to Bignum[2] | silently lossy past `2^53` (all `Number` is f64); **`BigInt`** is unbounded | 64-bit ints **wrap** (two's-complement)[4] | promotes to float (loses precision) | promotes to `float` (`PHP_INT_MAX`+1) | **wraps** (two's-complement); no panic | **debug: `panic`; release: wraps** (or `checked_*`/`wrapping_*`)[8] | arbitrary-precision (BEAM bignums) — none |
| **Assertion failure** | `AssertionError` (`assert`)[1] | (no core `assert`; test libs raise) | `console.assert` only logs; `node:assert` → `AssertionError` | `assert(false)` → raises the message[4] | `Carp::croak`/test libs | `AssertionError` (engine)[6] | (no built-in `assert`; test pkgs) | `assert!` → **`panic`** | `ExUnit.AssertionError` (test) |
| **I/O failure** | `OSError` subtree (`FileNotFoundError`, `PermissionError`, …)[1] | `IOError`, `Errno::*` (`SystemCallError`)[2] | rejected Promise / thrown `Error` with `.code` (`ENOENT`)[3] | `io.open` → **`(nil, errmsg)`** (no raise); `io.*` w/o check → error | `open` → false + `$!`; `die`s under autodie | `RuntimeException` / SPL or warning + `false` | `error` value (`*PathError`)[7] | `io::Error` in `Result` (`io::ErrorKind`)[8] | `{:error, reason}` tuple; `File.read!` → `File.Error` |
| **Value out of domain / parse failure** | `ValueError` (`int("x")`)[1] | `ArgumentError` (`Integer("x")`); `to_i` → `0` (lenient)[2] | `Number("x")` → **`NaN`** (sentinel); `parseInt` → `NaN`; `JSON.parse` → `SyntaxError`[3] | `tonumber("x")` → **`nil`**[4] | `"x"+0` → `0` (warns) | `intval` lenient; `json_decode` → `null`+error state | `strconv.Atoi` → `error` value[7] | `"x".parse::<i32>()` → `Err(ParseIntError)`[8] | `String.to_integer("x")` → **`ArgumentError`** |
| **"Not implemented / not supported"** | `NotImplementedError` (⊂ `RuntimeError`)[1] | `NotImplementedError` (⊂ `ScriptError`, **outside** `StandardError`!)[2] | (no standard type; throw custom `Error`) | (convention: `error("not implemented")`) | (custom `die`) | `BadMethodCallException` / `LogicException` | `errors.New("not implemented")` (sentinel idiom) | `unimplemented!()`/`todo!()` → **`panic`** | (raise custom or `RuntimeError`) |
| **Stack overflow (deep recursion)** | `RecursionError` (⊂ `RuntimeError`; soft limit)[1] | `SystemStackError` (**outside** `StandardError`)[2] | `RangeError: Maximum call stack size exceeded`[3] | `stack overflow` error (catchable via `pcall`)[4] | deep recursion warning; can exhaust → crash | `Error` ("Allowed memory" / segfault risk) | **fatal** `goroutine stack exceeds` (not recoverable) | **abort** (stack overflow is not a catchable panic) | process crash (let-it-crash) |

**Granularity philosophy this matrix reveals.**
- **Sentinel-by-default scripting (Lua, Perl, JS, PHP read, Ruby `[]`)** treats "absent element" as a
  normal value (`nil`/`undef`/`undefined`/`null`), reserving errors for *structural* faults (indexing
  a non-container, calling a nil). **Stash (and Python's `[]`/`d[k]`) raise instead** — the opposite
  philosophy.
- **Ruby's dual accessor** (`[]` lenient → `nil`, `fetch` strict → raises) is the explicit "you choose
  the strictness at the call site" model — a third option distinct from "always raise" / "always
  sentinel."
- **Integer-vs-float division by zero diverges sharply** even *within* a language: Lua/Ruby/PHP raise
  on integer `÷0` but return `inf`/`NaN` on float `÷0`; **Python raises on both**; JS (all-float
  `Number`) returns `Infinity` and only its `BigInt` integer type raises.
- **Integer overflow** splits four ways: **none/bignum** (Python, Ruby, Elixir), **wrap** (Lua, Go),
  **promote-to-float** (Perl, PHP), **panic-in-debug** (Rust). Stash's choice here is a *value-domain*
  question that surfaces as an error-type question only if it raises.

---

## 3. Per-language deep dives

### Python (full)
- **A. Root & catchability.** Everything derives from `BaseException`; the universal advice is to
  catch **`Exception`** (its largest subtree), because `BaseException` also parents the
  control-flow/system exits `SystemExit`, `KeyboardInterrupt`, `GeneratorExit` that you almost never
  want to swallow.[1] So the "catchable everyday root" is `Exception`, one level below the true root.
- **B. Hierarchy.** A genuinely **deep tree** where intermediate nodes earn their keep:
  `ArithmeticError ⊃ {ZeroDivisionError, OverflowError, FloatingPointError}`,
  `LookupError ⊃ {IndexError, KeyError}`, `OSError ⊃ {FileNotFoundError, PermissionError,
  TimeoutError, ConnectionError ⊃ …}`. `except LookupError` catches both index and key faults; `except
  OSError` catches the whole I/O family.[1]
- **C. Naming.** Uniform `*Error`. A parallel `Warning` subtree (not errors) is the only deviation.
- **D. Payload.** `args` tuple; `__traceback__`; **two cause links** — `__cause__` (explicit,
  `raise New from Orig`) and `__context__` (implicit, auto-set when one raises *while handling*
  another); `raise New from None` suppresses display of the context. `add_note()`/`__notes__`
  (3.11+).[1] This **dual implicit/explicit chain** is more nuanced than most languages.
- **E. Raise / define.** `raise Expr`; new types subclass `Exception` (or any subclass). You **cannot
  raise a non-exception** — `raise 42` is itself a `TypeError`.
- **F. Catch.** `try/except T as e/else/finally`. Matching is **by class (isa)**: `except T` catches
  `T` and every subclass — this is what makes the tree pay off. Tuples for multiple types; bare
  `raise` re-raises the active exception. No `retry`. Uncaught → traceback printed, nonzero exit.[1]
- **Notable choices.** (1) Catchable everyday-root one level below the true root, to keep system exits
  un-swallowed. (2) Implicit cause chaining (`__context__`) captures "error during error handling" for
  free. (3) Tree depth is deliberately exploited (`LookupError`, `OSError`).

### Ruby (full)
- **A. Root & catchability.** `Exception` is the root, but the catchable *default* is **`StandardError`**:
  a `rescue` clause with no class argument rescues `StandardError` and below — **not** `Exception`.[2]
  System-level faults (`SystemExit`, `NoMemoryError`, `SignalException`/`Interrupt`, `ScriptError`,
  `SystemStackError`) deliberately sit **outside `StandardError`**, so naïve `rescue => e` won't trap a
  `Ctrl-C` or an out-of-memory. This is the canonical "don't make the catch-all swallow the world"
  design, enforced by hierarchy *placement*.
- **B. Hierarchy.** Two-tier tree: `Exception → StandardError → {TypeError, ArgumentError,
  IndexError→KeyError, RangeError→FloatDomainError, ZeroDivisionError, IOError→EOFError,
  RuntimeError→FrozenError, NameError→NoMethodError, …}`.[2]
- **C. Naming.** Uniform `*Error`.
- **D. Payload.** `message`, `backtrace`, and **`cause`** — auto-populated: `raise` inside a `rescue`
  links the new exception's `cause` to the one being handled (same idea as Python's `__context__`).[2]
- **E. Raise / define.** `raise` (bare re-raises or makes a `RuntimeError`), `raise Class, "msg"`. New
  types subclass `StandardError` by convention. You raise an `Exception` class/instance, not arbitrary
  values.
- **F. Catch.** `begin/rescue T => e/else/ensure/end`, match **by class**. Distinctive: the **`retry`**
  keyword re-executes the `begin` block (built-in retry loop, rare among these languages). `ensure` =
  finally. Uncaught → message + backtrace, nonzero exit.[2]
- **Notable choices.** (1) `StandardError`-vs-`Exception` split = catchability encoded in hierarchy
  position. (2) First-class `retry`. (3) `NotImplementedError`/`SystemStackError` placed *outside*
  `StandardError` so they escape ordinary rescues — a deliberate "these are not your bug to handle."

### JavaScript / Node (full)
- **A. Root & catchability.** Single `Error`; **no system/ordinary split**. One `catch` binding traps
  everything thrown (including non-`Error` values).
- **B. Hierarchy.** **Flat.** `RangeError`, `ReferenceError`, `SyntaxError`, `TypeError`, `URIError`,
  `EvalError`, `AggregateError` all extend `Error` directly — there is **no intermediate grouping
  node** (no `LookupError`-equivalent).[3] Catch-supertype only works at the single `Error` level.
- **C. Naming.** Uniform `*Error`.
- **D. Payload.** `message`, `name`, **`cause`** (`new Error(msg, { cause })`, ES2022), de-facto
  `stack`. `AggregateError.errors` carries an **array** of errors (e.g. from `Promise.any`).[3]
- **E. Raise / define.** **`throw <anyValue>`** — strings, numbers, objects, anything. New types via
  `class X extends Error` (set `this.name`). The "throw anything" freedom is a notable looseness.
- **F. Catch.** `try/catch(e)/finally` with **one untyped binding**; you discriminate with
  **`e instanceof T`** or **`e.name`** (string). Optional-catch-binding `catch {}` (no var). No typed
  clauses, no retry. Uncaught → unhandled exception / `process` `uncaughtException` /
  `unhandledRejection`.[3]
- **Notable choices.** (1) Flat tree → matching is `instanceof`/string, not clause dispatch. (2) `name`
  is a *string* field, so "type identity" is partly stringly-typed. (3) `cause` standardized late
  (2022); `AggregateError` is the multi-error wrapper.

### Lua (full)
- **A. Root & catchability.** **No error type system whatsoever.** An error object is *any value*.[4]
- **B. Hierarchy.** None.
- **C. Naming.** n/a.
- **D. Payload.** Whatever you raised. Lua-generated errors are strings (`"chunk:line: message"`).
  `xpcall`'s handler can attach a `debug.traceback`.[4]
- **E. Raise / define.** **`error(value [, level])`** raises any value; `assert(v, msg)` raises when `v`
  is falsy. "Typed errors" = a table convention you invent (e.g. `{ code = …, msg = … }`).
- **F. Catch.** **`pcall(f, …)` → `(ok, resultOrError)`**; `xpcall(f, handler, …)` runs a handler
  before unwinding. **No typed catch, no finally** — you inspect the returned error value yourself
  (`type()`, fields, string match) and emulate cleanup with manual `pcall` wrapping. Uncaught
  propagates to the host (standalone interpreter prints it).[4]
- **Notable choices.** (1) Maximal minimalism: errors are just values + a boolean protocol. (2) The
  *consumer* decides all structure — zero language opinion. (3) Missing keys are `nil` (sentinel),
  reserving errors for structural faults (indexing/calling nil).

### Perl (full)
- **A. Root & catchability.** **No core hierarchy.** `die` with a string or any blessed object; the
  payload lands in **`$@`**.[5]
- **B. Hierarchy.** None in core (CPAN's `Exception::Class`/`Throwable` add one if you opt in).
- **C. Naming.** n/a in core.
- **D. Payload.** String message (Perl auto-appends `" at FILE line N.\n"` unless your message ends in
  `\n`) or an object's own fields. No standard cause chain in core.[5]
- **E. Raise / define.** `die LIST`; `croak`/`confess` (Carp) report from the caller's perspective.
  Define a type = bless a class and `die` an instance. Arbitrary scalars allowed.
- **F. Catch.** Classic **`eval { … }; if ($@) { … }`** (an empty `$@` means success), or `Try::Tiny`,
  or the newer native `try/catch`. Matching = inspect `$@`: `ref`, `blessed(...)->isa(...)`, or regex
  on the string. No type dispatch in core. Uncaught → `$@` to stderr, nonzero exit.[5]
- **Notable choices.** (1) `$@`-as-channel is a single global slot, easy to clobber (the classic
  `local $@` discipline). (2) String-or-object duality means every handler must first ask "is this a
  ref?". (3) Hierarchy is entirely a CPAN/userland concern.

### PHP (full)
- **A. Root & catchability.** **`Throwable`** interface at the root with a deliberate **two-branch
  split**: **`Error`** (engine/internal faults that were fatal pre-PHP-7) and **`Exception`**
  (userland). `catch (Throwable)` spans both; `catch (Exception)` **misses every `Error`** — the
  migration footgun.[6]
- **B. Hierarchy.** A real tree under each branch: `Error → {TypeError→ArgumentCountError, ValueError,
  ArithmeticError→DivisionByZeroError, AssertionError, UnhandledMatchError, CompileError→ParseError}`;
  `Exception → {RuntimeException, LogicException→{InvalidArgumentException, OutOfRangeException, …},
  ErrorException}`. (Note the SPL trap: `OutOfRangeException` ⊂ `LogicException` vs `OutOfBoundsException`
  ⊂ `RuntimeException` — *different* types, easy to confuse.)[6]
- **C. Naming.** **Mixed by branch** — engine errors use `*Error`, userland uses `*Exception`. The two
  conventions coexist by design, which is itself instructive (the split is visible in the *name*).
- **D. Payload.** `getMessage()`, `getCode()`, **`getPrevious()`** (cause chain via the 3rd
  constructor arg `$previous`), `getTrace()`, `getFile()`, `getLine()`.[6]
- **E. Raise / define.** `throw new T(...)`. User types subclass `Exception` (or `Error`). **Only
  `Throwable` instances may be thrown** — no arbitrary values.
- **F. Catch.** `try/catch (T $e)/finally`; match **by type**; **multi-catch `catch (A|B $e)`**; re-throw
  `throw $e`. Uncaught → fatal error / `set_exception_handler`. No retry.[6]
- **Notable choices.** (1) The `Error`/`Exception` split makes "engine bug vs my exception" a
  *type-system* distinction — analogous to Ruby's `StandardError`-vs-`Exception`, but here it's two
  sibling subtrees under one interface, and it's **reflected in the naming suffix**. (2) `Throwable` as
  an *interface* (not a class) keeps the root abstract.

### Go (contrast — brief)
Errors are **values**, not exceptions: the `error` interface (`Error() string`); functions return
`(T, error)` and callers write `if err != nil`.[7] No hierarchy — grouping is via **wrapping**
(`fmt.Errorf("…: %w", err)`), matched with **`errors.Is`** (sentinel through wraps) and **`errors.As`**
(type through wraps). `panic`/`recover` are reserved for programmer-bug/exceptional conditions (nil
deref, index OOB, integer ÷0) — *not* ordinary failure. **Relevance to Stash:** the canonical
**values-not-exceptions** pole, and a flat/open model where the cause chain is the only "tree."

### Rust (contrast — brief)
Two channels by design: **`Result<T, E>`** for recoverable errors (a returned value, propagated with
**`?`**) and **`panic!`** for unrecoverable ones (unwind/abort).[8] No exception hierarchy; the
`std::error::Error` trait (`Debug + Display`) is implemented per type, with **`source()`** giving the
cause chain.[9] `unwrap()`/`expect()` turn `Err`/`None` into a `panic`. **Relevance to Stash:** makes
the **recoverable-vs-unrecoverable split a *type* distinction** (Result vs panic) rather than a
hierarchy distinction — a different axis than catch-supertype depth.

### Elixir / Erlang (contrast — brief)
Three coexisting models: **`{:ok, _}`/`{:error, reason}` tuples** for expected failures (with a `!`
bang-variant like `File.read!` that raises instead), **`raise`** of exception **structs** for the
exceptional path, and **"let it crash"** — an unhandled error kills the process and a supervisor
restarts it, so defensive `rescue` is often discouraged.[10] `raise "msg"` → `RuntimeError`; `defexception`
defines a struct (with a `:message` field); `try/rescue T/catch/after`, `reraise e, __STACKTRACE__`.
**Relevance to Stash:** the **bang-convention** is a clean way to offer *both* a sentinel/tuple channel
and a raising channel for the *same* operation, chosen at the call site (cf. Ruby `[]` vs `fetch`).

---

## 4. Design lessons for Stash, mapped to the axes

For each axis: the **options** the survey demonstrates and the **tradeoff** each implies for a
flat-~25-type language like Stash. **These are options and tradeoffs only — no recommendation. The
four flagged questions (i)–(iv) are surfaced as open, with the human reserving the call.**

### A. Root type & catchability — *(flagged: should the base be catchable, and how deep does a hierarchy buy its keep?)*
Stash today: single `RuntimeError` base; all built-ins derive from it. The survey shows **three
patterns for the catchable root**:
- **One flat catchable root** (JS `Error`, Lua's "any value", Stash's `RuntimeError`). Simplest;
  `catch RuntimeError` (or its Stash equivalent) traps everything. Cost: no way to *exclude* a class of
  "you shouldn't catch this" faults via the type system.
- **Catchable root one level below the true root** (Python `Exception` under `BaseException`). Buys a
  designated "everyday catch-all" while keeping control-flow/system signals (exit, cancellation,
  interrupt) *out* of it.
- **Catchability encoded by hierarchy placement** (Ruby `StandardError` vs `Exception`; PHP `Error` vs
  `Exception` under `Throwable`). The bare/idiomatic catch deliberately *misses* system-level faults.
- **Tradeoff for Stash specifically.** Stash already has `CancellationError` and `ExitException`
  (`exit`) in the runtime. The live question this raises: **should `catch RuntimeError` (the broad
  catch) trap `CancellationError`/exit, or should those sit *outside* the everyday-catchable root** (the
  Python/Ruby move)? A flat single root makes "swallowing cancellation by accident" easy; a one-notch
  split prevents it at the cost of one hierarchy level and a documented rule. **Open — reserved for the
  design session.**

### B. Hierarchy shape — *(flagged with A: how deep does a hierarchy buy its keep?)*
- **Deep tree** (Python, Ruby, PHP): enables **catch-supertype** — `except LookupError` catches index
  *and* key faults; `except OSError` catches the whole I/O family. Each intermediate node is only
  worth adding if a real call site wants to catch *exactly that group*. Cost: depth, more names, a
  learning surface.
- **Flat** (JS, Stash today): simplest mental model; matching is per-leaf-type or the single root, with
  nothing in between. Cost: no "catch this family but not that one" without listing every member.
- **The concrete payoff test** (from the data): a grouping node earns its keep iff some handler would
  realistically write `catch <Group>` to trap several leaves at once. Candidate Stash groupings the
  survey suggests *considering* (not adopting): a `LookupError`-style parent over `IndexError` +
  (future) `KeyError`; an `ArithmeticError`-style parent if Stash grows multiple numeric faults; an
  I/O family parent over `IOError` + `CommandError` + `TimeoutError`. Each is an **option**; whether any
  clears the payoff bar is the design call.
- **Stash-specific note.** With ~25 types already flat, adding *a few* grouping parents is a small,
  reversible step; going fully Python-deep is a larger commitment. The data does **not** say which —
  it says "add a grouping node only where a real `catch <group>` site exists."

### C. Naming convention — *(flagged: naming discipline)*
- **Uniform `*Error`** (Python, Ruby, JS, Stash today, Elixir structs): one suffix, zero ambiguity at a
  glance. This is the dominant scripting convention and Stash already matches it.
- **Mixed `*Error`/`*Exception`** (PHP, and the broader Java/C# world): the suffix *encodes a category*
  (engine vs userland). Expressive, but you must remember which suffix a given type uses.
- **`*Err`/sentinels** (Go, Rust): terser, fits a values-not-exceptions world.
- **Tradeoff for Stash.** Stash's `[StashError]` machinery already defaults the Stash-facing name to the
  C# class name and treats `Name=` overrides as a code smell — i.e. the codebase *already enforces*
  naming discipline mechanically. The open question is narrow: **keep the single `*Error` suffix
  universally** (simplest; current state), or ever introduce a second suffix to signal a category (e.g.
  un-catchable system faults) — the PHP precedent shows this *can* carry meaning but costs uniformity.
  **Open.**

### D. Payload / cause-chaining — *(flagged: cause-chaining as a payload feature)*
Stash today: per-type structured fields via `[StashError(Properties, PropertyTypes)]` + a `Description`.
That already matches or exceeds the *structured-field* richness of most surveyed languages (only
Python's per-type `OSError.errno`/`filename` and PHP's per-class fields are comparable; JS/Lua/Perl
carry far less). The **gap the survey highlights is the cause chain**, which Stash does not obviously
model:
- **Explicit-only chain** (JS `cause`, PHP `getPrevious`, Rust `source`, Go `%w`): the raiser opts in by
  passing the underlying error. Simple, predictable.
- **Explicit + implicit chain** (Python `__cause__` *and* `__context__`; Ruby auto-`cause`): the runtime
  *also* records "this error was raised while handling that one" for free — great for diagnostics,
  slightly more machinery and a "suppress" escape hatch (`from None`).
- **No chain** (Lua, core Perl): the consumer wires any context manually.
- **Tradeoff for Stash.** A `cause` field (explicit, JS/PHP-style) is a small, self-contained payload
  addition that makes "wrap a low-level `IOError` in a domain error" first-class — and it composes with
  Stash's existing per-type `Properties`. Whether to *also* capture an implicit "error-during-handling"
  context (Python/Ruby) is a separate, larger choice with a display-suppression wrinkle. Both are
  **options**; neither is recommended here.

### E. Built-in vs user-raised / user-defined
- **Only exception objects raisable** (Python, PHP, Stash today via typed `RuntimeError` subclasses):
  every thrown thing has a known shape; handlers can rely on `.message` etc. Cost: a tiny bit of
  ceremony to raise.
- **Any value raisable** (Lua, Perl, JS): maximal flexibility, but every handler must first ask "what
  did I even catch?" and normalize. The survey suggests this looseness is a *frequent source of
  fragile handlers* (JS's `throw "string"`, Perl's string-or-object `$@`).
- **User-defined types** — universally supported by subclassing the base (Python/Ruby/PHP/Elixir
  `defexception`). Stash already supports user-raised errors; the open question is whether **user types
  may subclass built-ins** (to inherit catch-supertype behavior) or only the root.
- **Tradeoff for Stash.** Stash's "raise a typed error" model is already in the strict/safe camp; the
  design choice is mostly about **user-defined type ergonomics** (can a user error sit *under* an
  existing group and be caught by `catch <group>`?), which is downstream of the §B hierarchy decision.

### F. Catch / recovery semantics
- **Match by class/isa** (Python, Ruby, PHP, Stash today): clauses dispatch on type; catch-supertype
  falls out of §B. The mainstream choice; Stash already does this.
- **Single untyped binding + manual discrimination** (JS `instanceof`/`name`, Lua/Perl inspect-the-value):
  simpler grammar, pushes matching into the handler body; stringly-typed in the `name`/regex variants.
- **finally/ensure** — present in Python/Ruby/JS/PHP/Elixir; absent in Lua/core-Perl (emulated). A
  cross-cutting expectation; relevant only as a checklist item for Stash's `try` grammar.
- **`retry`** — Ruby's distinctive built-in (re-run the protected block); rare elsewhere. An *option* if
  Stash ever wants first-class retry, but clearly not load-bearing for a taxonomy.
- **Uncaught-at-top-level** — every language prints a message/trace and exits nonzero; the *negative
  space* (what a Stash uncaught error guarantees to print, and its exit code) is normative behavior the
  spec should state regardless of taxonomy.
- **Tradeoff for Stash.** Match-by-class is already chosen and aligns with the mainstream; the only
  taxonomy-coupled point is that **richer §B grouping directly increases `catch` expressiveness** (you
  can only `catch <group>` if `<group>` exists).

### G. The situation→type map — granularity philosophy
- **Raise-on-absence** (Python `[]`/`d[k]`, Stash `IndexError`/`KeyError`) vs **sentinel-on-absence**
  (Lua/Perl/JS/PHP-read/Ruby `[]`): the single biggest philosophical fork. Stash currently **raises** —
  consistent with Python, opposite to most scripting peers. The survey's most reusable idea here is
  **Ruby's/Elixir's dual accessor** (`[]`→`nil` vs `fetch`/`fetch!`→raise; bang-variants): offer *both*
  channels for the same operation and let the **call site** pick strictness. That's an option for Stash
  that sidesteps the "always raise vs always sentinel" dilemma — **surfaced, not recommended.**
- **Domain-fault granularity.** Stash's flat ~25 already names most situations the survey enumerates
  (TypeError, ValueError, IndexError, IOError, ParseError, NotSupportedError, TimeoutError). The cells
  where peers **split finer** and Stash *could* consider a distinct type: **missing-key** (Python/Ruby
  have a dedicated `KeyError` distinct from `IndexError`; Stash appears to have only `IndexError`),
  **undefined-name vs undefined-attribute/method** (Python `NameError` vs `AttributeError`; Ruby
  `NameError` vs `NoMethodError`), and **division-by-zero / arithmetic** (most peers have a dedicated
  `ZeroDivisionError`/`ArithmeticError`; Stash has **neither** — division by zero throws the **bare
  base `RuntimeError`** with a `"Division by zero."` string message, on **both** integer and float
  `÷0`, per `Stash.Bytecode/VM/VirtualMachine.Arithmetic.cs` and `RuntimeOps.cs`, so it is *currently
  uncatchable by a specific type* and matches Python's strict-on-both stance only by accident of the
  base type). Each finer split is an **option** justified only if handlers want to distinguish it; the
  arithmetic case is also a present *gap* (a failure with no dedicated type, caught only via the broad
  base).
- **The cells most likely to be *value-domain* not *error-type* questions** — integer overflow and
  float-÷0 — are flagged in §2: these become taxonomy questions only *if* Stash chooses to raise.

### (iv) Values-vs-exceptions — *is it even live for Stash?*
**Largely settled, by the survey's own logic.** Stash already has exceptions (typed `try`/`catch`,
~25 error types). Go's pure values-as-errors model and Rust's `Result`/`panic` split are **not a live
either/or** for a language that already committed to exceptions — adopting them wholesale would be a
re-architecture, not a taxonomy decision. The *residual* live question the contrast trio surfaces is
narrower and worth stating: **does Stash also want a lightweight value/sentinel channel for *expected*
failures** — i.e. an Elixir-style `!`/non-`!` or Ruby-style `[]`/`fetch` pairing, or a `Result`-like
return for operations where "absent/failed" is ordinary rather than exceptional? That is the genuinely
open slice of axis (iv); the broad "exceptions or values?" framing is closed for Stash. **Surfaced as
an option; the call is reserved.**

---

## 5. Sources

All sources are official language documentation except where a specific behavioral cell required a
secondary reference (noted). Fetched June 2026.

1. Python — *Built-in Exceptions* (full hierarchy, per-type semantics, cause chaining):
   <https://docs.python.org/3/library/exceptions.html>
2. Ruby — *Exception class* (hierarchy, `rescue` defaults to `StandardError`, `cause`, payload):
   <https://docs.ruby-lang.org/en/3.3/Exception.html>
3. JavaScript — MDN *Error* (flat subtypes, `throw` any value, `cause`, `AggregateError`,
   `instanceof`/`name` matching): <https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Error>
4. Lua 5.4 — *Reference Manual* (`error`, `pcall`/`xpcall`, `assert`, missing-key=`nil`, integer
   `//0` error vs float `inf`/`nan`, arbitrary error values): <https://www.lua.org/manual/5.4/manual.html>
5. Perl — *perlfunc: die* (`$@`, string-or-object exceptions, `eval` catch):
   <https://perldoc.perl.org/functions/die>
6. PHP — *Errors in PHP 7+* (`Throwable` with `Error`/`Exception` split, multi-catch, `getPrevious`):
   <https://www.php.net/manual/en/language.errors.php7.php>
7. Go — *Error handling and Go* (errors-as-values, `error` interface, wrapping, `panic`/`recover`):
   <https://go.dev/blog/error-handling-and-go>
8. Rust — *The Rust Book, Ch. 9: Error Handling* (`Result` vs `panic!`, `?`, index panic, overflow
   debug-panic): <https://doc.rust-lang.org/book/ch09-00-error-handling.html>
9. Rust — *std::error::Error trait* (`source()` cause chain, `Debug + Display`):
   <https://doc.rust-lang.org/std/error/trait.Error.html>
10. Elixir — *try, catch, and rescue* (tuples vs bang functions, `raise`/`defexception`, `rescue`,
    `reraise`, let-it-crash): <https://elixir.hexdocs.pm/try-catch-and-rescue.html>
11. Ruby — *Array class* (`arr[i]`→`nil` out of bounds; `Array#fetch` raises `IndexError`):
    <https://docs.ruby-lang.org/en/3.3/Array.html>
12. PHP — *DivisionByZeroError* and *Undefined array key* warning (read of missing key → `null` +
    Warning, not an exception; `intdiv`/`%`/`/` throw `DivisionByZeroError`):
    <https://www.php.net/manual/en/class.divisionbyzeroerror.php>
13. Python — `NameError` (undefined name) vs `AttributeError` (undefined attribute/method) — per the
    Built-in Exceptions reference [1]; secondary confirmation:
    <https://runebook.dev/en/docs/python/library/exceptions/AttributeError.name>
14. Ruby — `ZeroDivisionError` for integer `1/0`; float `1.0/0` → `Infinity` (numeric type governs):
    <https://docs.ruby-lang.org/en/3.3/ZeroDivisionError.html>
15. JavaScript — *RangeError: BigInt division by zero* (Number `1/0`→`Infinity`; BigInt `1n/0n` raises):
    <https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Errors/BigInt_division_by_zero>
16. Python — integer arbitrary-precision (no `OverflowError` from int arithmetic; `OverflowError` arises
    for float ops / int-too-large-for-float) — per [1]; secondary:
    <https://docs.python.org/3/library/exceptions.html#OverflowError>
