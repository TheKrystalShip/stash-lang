# Stash — Testing Infrastructure

> **Status:** Draft v0.1
> **Created:** March 2026
> **Purpose:** Source of truth for Stash's built-in testing primitives, TAP output, and test harness architecture.
>
> **Companion documents:**
>
> - [Language Specification](../Stash%20—%20Language%20Specification.md) — language syntax, type system, interpreter architecture
> - [Standard Library Reference](../Stash%20—%20Standard%20Library%20Reference.md) — built-in namespace functions
> - [DAP — Debug Adapter Protocol](DAP%20—%20Debug%20Adapter%20Protocol.md) — debug adapter server
> - [LSP — Language Server Protocol](LSP%20—%20Language%20Server%20Protocol.md) — language server

---

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture](#2-architecture)
3. [Running Tests](#3-running-tests)
4. [Test Registration](#4-test-registration)
5. [`assert` Namespace](#5-assert-namespace)
6. [TAP Output](#6-tap-output)
7. [Output Capture](#7-output-capture)
8. [Setup & Teardown Hooks](#8-setup--teardown-hooks)
9. [Implementation Details](#9-implementation-details)
10. [Future Extensions](#10-future-extensions)

---

## 1. Overview

Stash provides built-in testing primitives that enable structured, tooling-friendly test execution. Test scripts are ordinary Stash scripts — no special file format, no magic. `test.it()` and `assert.equal()` are regular function calls. The language provides the hooks; users can build full testing frameworks on top.

By default, runtime errors crash the script. The testing harness changes this: in test mode (`--test`), assertion failures inside `test.it()` blocks are caught and recorded rather than crashing. Execution continues to the next test, collecting all results.

**Key design goals:**

- **TAP compliance** — output integrates directly with CI/CD pipelines and standard reporting tools
- **Zero overhead** — no cost when not in test mode; the harness field is a null-checked optional
- **Structured assertion errors** — every failure captures expected vs. actual values and source location
- **Composability** — built on top of the existing interpreter architecture with no special-casing in the core

---

## 2. Architecture

### Parallel with the Debugger

The testing infrastructure follows the same architectural pattern as the [debugging hooks](../Stash%20—%20Language%20Specification.md#11-debugging-support). The interpreter holds an `ITestHarness?` field alongside `IDebugger?`, guarded by the same null-check pattern for zero overhead during normal execution.

| Debugging                        | Testing                               |
| -------------------------------- | ------------------------------------- |
| `IDebugger` interface            | `ITestHarness` interface              |
| `CliDebugger` (interactive)      | `TapReporter` (TAP output)            |
| `DebugSession` (DAP for VS Code) | Future IDE test explorer adapter      |
| `--debug` CLI flag               | `--test` CLI flag                     |
| `OnBeforeExecute()` hook         | `OnTestStart()` / `OnTestEnd()` hooks |
| `OnError()` hook                 | `OnAssertionFail()` hook              |
| Zero overhead when null          | Zero overhead when null               |

### `ITestHarness` Interface

```csharp
public interface ITestHarness
{
    void OnTestStart(string name, SourceSpan span);
    void OnTestPass(string name, TimeSpan duration);
    void OnTestFail(string name, string message, SourceSpan span, TimeSpan duration);
    void OnTestSkip(string name, string? reason);
    void OnAssertionFail(object? expected, object? actual, string message, SourceSpan span);
    void OnSuiteStart(string name);
    void OnSuiteEnd(string name, int passed, int failed, int skipped);
}
```

### `TapReporter`

`TapReporter` implements `ITestHarness` and emits TAP-compliant output to stdout. It tracks test count and result state to produce the plan line (`1..N`) and individual `ok` / `not ok` lines. Located in `Stash.Tap/`.

---

## 3. Running Tests

```bash
stash --test math_test.stash          # Run tests with TAP output
stash --test tests/                    # Run all .stash files in directory
```

The `--test` flag activates test mode: a `TapReporter` is attached as the test harness, execution proceeds normally, and `test.it()` calls run through the harness. Scripts not using `test.it()` can still be run with `--test` without side effects.

---

## 4. Test Registration

### `test.it(name, fn)` — Register and Run a Test Case

```stash
test.it("addition works", () => {
    assert.equal(1 + 1, 2);
});
```

Each `test.it()` call:

1. Notifies the harness that a test is starting.
2. Executes the lambda in an error-catching wrapper.
3. Reports pass or fail to the harness.
4. **Continues execution** — failures do not crash the script.

### `test.describe(name, fn)` — Group Tests

```stash
test.describe("math operations", () => {
    test.it("addition", () => {
        assert.equal(2 + 3, 5);
    });
    test.it("multiplication", () => {
        assert.equal(3 * 4, 12);
    });
});
```

`test.describe()` blocks produce hierarchical test names (e.g., `math operations > addition`). Nesting is supported.

### `test.skip(name, fn)` — Register a Skipped Test

```stash
test.skip("not yet implemented", () => {
    // Body is never executed
    assert.fail("should not reach here");
});
```

`test.skip()` registers a test that is intentionally not run. The test body is provided for documentation but is never executed. In TAP output, skipped tests emit `ok N - <name> # SKIP skipped`.

Use `test.skip()` for:

- Work-in-progress tests that aren't ready yet
- Tests for features that are known broken
- Platform-specific tests that don't apply to the current environment

### `test.only(name, fn)` — Exclusive Test

```stash
test.only("critical path", () => {
    assert.equal(compute(), 42);
});
test.it("other test", () => {
    // This will be skipped — test.only is active
});
```

When one or more `test.only()` calls appear in a test run, all `test.it()` calls are skipped (counted as skipped in TAP output). `test.skip()` tests remain skipped as normal. This mirrors the behavior of `it.only` / `test.only` in Jest.

Use `test.only()` to:

- Focus on a single failing test during debugging
- Run a subset of tests without commenting out others

### `test` Namespace Functions Summary

| Function                  | Description                                                             |
| ------------------------- | ----------------------------------------------------------------------- |
| `test.it(name, fn)`       | Register and run a test case                                            |
| `test.only(name, fn)`     | Exclusive test — only `test.only` tests run when any exist              |
| `test.skip(name, fn)`     | Register a skipped test (body is not executed)                          |
| `test.describe(name, fn)` | Group tests under a descriptive name                                    |
| `test.beforeAll(fn)`      | Run `fn()` once before tests in the current `test.describe` block       |
| `test.afterAll(fn)`       | Run `fn()` once after all tests in the current `test.describe` block    |
| `test.beforeEach(fn)`     | Run `fn()` before each `test.it()` in the current `test.describe` scope |
| `test.afterEach(fn)`      | Run `fn()` after each `test.it()` in the current `test.describe` scope  |
| `test.captureOutput(fn)`  | Execute `fn()` with output redirected; returns captured string          |

These functions are accessed via the `test` namespace.

---

## 5. `assert` Namespace

| Function                                  | Description                                            |
| ----------------------------------------- | ------------------------------------------------------ | ----------------- | --------- |
| `assert.equal(actual, expected)`          | Assert `actual == expected` (no type coercion)         |
| `assert.notEqual(actual, expected)`       | Assert `actual != expected`                            |
| `assert.true(value)`                      | Assert value is truthy                                 |
| `assert.false(value)`                     | Assert value is falsy                                  |
| `assert.null(value)`                      | Assert value is `null`                                 |
| `assert.notNull(value)`                   | Assert value is not `null`                             |
| `assert.greater(a, b)`                    | Assert `a > b`                                         |
| `assert.less(a, b)`                       | Assert `a < b`                                         |
| `assert.throws(fn)`                       | Assert `fn()` throws; returns error message            |
| `assert.fail(message?)`                   | Unconditionally fail                                   |
| `assert.deepEqual(actual, expected)`      | Recursive structural equality (arrays, dicts, structs) |
| `assert.closeTo(actual, expected, delta)` | Assert `                                               | actual - expected | <= delta` |

> **Note:** Equality uses Stash's strict equality — no type coercion. For truthiness rules, see the [Language Specification](../Stash%20—%20Language%20Specification.md#4-type-system).

### Assertion Error Format

When an assertion fails, the error message includes the expected and actual values along with the source location:

```
assert.equal failed: expected 42 but got 17
  at test_math.stash:14:5
```

When running **outside test mode** (no `--test`), assertion failures are regular `RuntimeError` exceptions — the script crashes on first failure. This makes assertions usable as lightweight guards in non-test scripts.

---

## 6. TAP Output

Stash emits [TAP version 14](https://testanything.org/tap-version-14-specification.html) — a standard protocol supported by CI/CD systems and reporting tools (Jest, prove, tap-junit, GitHub Actions, etc.).

```
TAP version 14
1..3
ok 1 - math operations > addition
not ok 2 - string equality
  ---
  message: "assert.equal failed: expected \"hello\" but got \"world\""
  severity: fail
  at:
    file: test_strings.stash
    line: 14
    column: 5
  ...
ok 3 - null handling
```

**Format rules:**

- The plan line (`1..N`) is emitted after all tests complete, once the total count is known.
- Passing tests emit `ok N - <name>`.
- Failing tests emit `not ok N - <name>` followed by a YAML diagnostics block.
- Skipped tests emit `ok N - <name> # SKIP <reason>`.

---

## 7. Output Capture

`test.captureOutput(fn)` temporarily redirects the interpreter's output writer to an in-memory string buffer, executes `fn()`, then restores the original writer and returns the captured string.

```stash
let output = test.captureOutput(() => {
    io.println("hello");
    io.print("world");
});
assert.equal(output, "hello\nworld");
```

This allows tests to assert on what a function prints without polluting the TAP output stream. Captured output does not appear in TAP results.

---

## 8. Setup & Teardown Hooks

Lifecycle hooks provide setup and teardown logic scoped to `test.describe()` blocks. All four hooks must be called inside a `test.describe()` block — using them at the top level throws a `RuntimeError`.

### `test.beforeAll(fn)` — One-Time Setup

```stash
test.describe("database tests", () => {
    let items = [];

    test.beforeAll(() => {
        arr.push(items, "initialized");
    });

    test.it("items is initialized", () => {
        assert.equal(items[0], "initialized");
    });
});
```

`test.beforeAll(fn)` executes `fn()` immediately when encountered. Since Stash executes synchronously and top-to-bottom, placing `test.beforeAll()` at the top of a `test.describe()` block ensures it runs before any tests.

### `test.afterAll(fn)` — One-Time Teardown

```stash
test.describe("resources", () => {
    test.afterAll(() => {
        // Runs after all tests in this describe block finish
    });

    test.it("uses resource", () => { /* ... */ });
});
```

`test.afterAll(fn)` registers `fn` to run when the `test.describe()` block ends. It runs in the `finally` block, so it executes even if a test fails.

### `test.beforeEach(fn)` / `test.afterEach(fn)` — Per-Test Hooks

```stash
test.describe("counter", () => {
    let count = [0];

    test.beforeEach(() => {
        count[0] = 0;  // Reset before each test
    });

    test.afterEach(() => {
        // Runs after each test body completes
    });

    test.it("starts at zero", () => {
        assert.equal(count[0], 0);
    });

    test.it("is reset between tests", () => {
        assert.equal(count[0], 0);
    });
});
```

### Hook Inheritance in Nested Describes

Hooks from parent `test.describe()` blocks are inherited by nested blocks. `test.beforeEach` hooks run outermost-to-innermost; `test.afterEach` hooks run innermost-to-outermost:

```stash
test.describe("outer", () => {
    test.beforeEach(() => { /* runs first */ });
    test.afterEach(() => { /* runs last */ });

    test.describe("inner", () => {
        test.beforeEach(() => { /* runs second */ });
        test.afterEach(() => { /* runs first in cleanup */ });

        test.it("example", () => {
            // Execution order:
            // 1. outer beforeEach
            // 2. inner beforeEach
            // 3. test body
            // 4. inner afterEach
            // 5. outer afterEach
        });
    });
});
```

Hooks do **not** leak between sibling `test.describe()` blocks — each block manages its own hook scope.

---

## 9. Implementation Details

### Project Structure

```
Stash.Core/
├── Runtime/
│   ├── ITestHarness.cs        # Interface — 7 callback methods
│   └── AssertionError.cs      # Structured assertion failure exception
Stash.Tap/
└── TapReporter.cs         # TAP output implementation
Stash.Stdlib/
└── BuiltIns/
    └── TestBuiltIns.cs        # test.it(), test.skip(), test.describe(), hooks, assert.*, test.captureOutput()
```

The `test.it()`, `test.skip()`, `test.describe()`, lifecycle hooks, and the `assert` namespace are registered in `TestBuiltIns.cs`, following the same pattern as other [built-in function registries](../Stash%20—%20Standard%20Library%20Reference.md#overview).

### Integration Points

- **CLI (`Program.cs`)** — The `--test` flag instantiates a `TapReporter` and assigns it to the VM's test harness before script execution.
- **VM** — Exposes `ITestHarness? TestHarness { get; set; }` via `IInterpreterContext`. All harness calls are guarded: `TestHarness?.OnTestStart(...)`.
- **`test.it()` built-in** — Catches `RuntimeError` and `AssertionException` inside the test lambda and routes them to `OnTestFail`. Runs `beforeEach`/`afterEach` hooks around the test body. All other exceptions propagate normally.
- **`test.skip()` built-in** — Calls `OnTestSkip` on the harness without executing the test body.
- **`test.describe()` built-in** — Pushes hook layers on entry and pops them on exit. Runs `afterAll` hooks in a `finally` block.
- **Lifecycle hooks** — `beforeAll` executes immediately. `afterAll` defers to the `test.describe` `finally` block. `beforeEach`/`afterEach` append to a per-`test.describe` hook stack; `test.it()` iterates all levels.
- **`assert.*` functions** — In test mode, throw `AssertionException` (caught by the `test.it()` wrapper). Outside test mode, throw `RuntimeError` (crashes the script).

---

## 10. Future Extensions

- **`test.only(name, fn)`** — Run only this test (for focused debugging); all other tests are skipped.
- **`assert.deepEqual(a, b)`** — Recursive equality for arrays, dicts, and struct instances.
- **`assert.closeTo(a, b, delta)`** — Float comparison with tolerance (avoids floating-point precision issues).
- **`--test-format=tap|json|junit`** — Alternate output formats for different CI/CD consumers.
- **Test discovery** — `stash --test tests/` discovers and runs all `*_test.stash` or `test_*.stash` files automatically.

### Complete Example

```stash
#!/usr/bin/env stash

import { deploy, Server } from "deploy.stash";

test.describe("deployment", () => {
    let srv = null;

    test.beforeEach(() => {
        srv = Server { host: "localhost", port: 22, status: "unknown" };
    });

    test.afterAll(() => {
        srv = null;  // Cleanup
    });

    test.it("creates server instance", () => {
        assert.equal(srv.host, "localhost");
        assert.equal(srv.port, 22);
    });

    test.it("deploy returns boolean", () => {
        let result = deploy(srv, "app.tar.gz");
        assert.equal(typeof(result), "bool");
    });

    test.skip("rollback not implemented yet", () => {
        let result = rollback(srv);
        assert.true(result);
    });
});

test.describe("language features", () => {
    test.it("null coalescing works", () => {
        let val = null ?? "default";
        assert.equal(val, "default");
    });

    test.it("type coercion does not happen", () => {
        assert.notEqual(5, "5");
        assert.notEqual(0, false);
        assert.notEqual(0, null);
    });
});
```

Running: `stash --test deploy_test.stash`

---

_This is a living document. Update as the testing infrastructure evolves._
