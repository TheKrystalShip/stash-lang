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
8. [Implementation Details](#8-implementation-details)
9. [Future Extensions](#9-future-extensions)

---

## 1. Overview

Stash provides built-in testing primitives that enable structured, tooling-friendly test execution. Test scripts are ordinary Stash scripts — no special file format, no magic. `test()` and `assert.equal()` are regular function calls. The language provides the hooks; users can build full testing frameworks on top.

By default, runtime errors crash the script. The testing harness changes this: in test mode (`--test`), assertion failures inside `test()` blocks are caught and recorded rather than crashing. Execution continues to the next test, collecting all results.

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

`TapReporter` implements `ITestHarness` and emits TAP-compliant output to stdout. It tracks test count and result state to produce the plan line (`1..N`) and individual `ok` / `not ok` lines. Located in `Stash.Interpreter/Testing/`.

---

## 3. Running Tests

```bash
stash --test math_test.stash          # Run tests with TAP output
stash --test tests/                    # Run all .stash files in directory
```

The `--test` flag activates test mode: a `TapReporter` is attached as the test harness, execution proceeds normally, and `test()` calls run through the harness. Scripts not using `test()` can still be run with `--test` without side effects.

---

## 4. Test Registration

### `test(name, fn)` — Register and Run a Test Case

```stash
test("addition works", () => {
    assert.equal(1 + 1, 2);
});
```

Each `test()` call:

1. Notifies the harness that a test is starting.
2. Executes the lambda in an error-catching wrapper.
3. Reports pass or fail to the harness.
4. **Continues execution** — failures do not crash the script.

### `describe(name, fn)` — Group Tests

```stash
describe("math operations", () => {
    test("addition", () => {
        assert.equal(2 + 3, 5);
    });
    test("multiplication", () => {
        assert.equal(3 * 4, 12);
    });
});
```

`describe()` blocks produce hierarchical test names (e.g., `math operations > addition`). Nesting is supported.

### Global Test Functions Summary

| Function             | Description                                                    |
| -------------------- | -------------------------------------------------------------- |
| `test(name, fn)`     | Register and run a test case                                   |
| `describe(name, fn)` | Group tests under a descriptive name                           |
| `captureOutput(fn)`  | Execute `fn()` with output redirected; returns captured string |

These are global functions (not namespaced) because they are used at the top level of test scripts.

---

## 5. `assert` Namespace

| Function                            | Description                                    |
| ----------------------------------- | ---------------------------------------------- |
| `assert.equal(actual, expected)`    | Assert `actual == expected` (no type coercion) |
| `assert.notEqual(actual, expected)` | Assert `actual != expected`                    |
| `assert.true(value)`                | Assert value is truthy                         |
| `assert.false(value)`               | Assert value is falsy                          |
| `assert.null(value)`                | Assert value is `null`                         |
| `assert.notNull(value)`             | Assert value is not `null`                     |
| `assert.greater(a, b)`              | Assert `a > b`                                 |
| `assert.less(a, b)`                 | Assert `a < b`                                 |
| `assert.throws(fn)`                 | Assert `fn()` throws; returns error message    |
| `assert.fail(message?)`             | Unconditionally fail                           |

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

`captureOutput(fn)` temporarily redirects the interpreter's output writer to an in-memory string buffer, executes `fn()`, then restores the original writer and returns the captured string.

```stash
let output = captureOutput(() => {
    io.println("hello");
    io.print("world");
});
assert.equal(output, "hello\nworld");
```

This allows tests to assert on what a function prints without polluting the TAP output stream. Captured output does not appear in TAP results.

---

## 8. Implementation Details

### Project Structure

```
Stash.Interpreter/
├── Testing/
│   ├── ITestHarness.cs        # Interface — 7 callback methods
│   └── TapReporter.cs         # TAP output implementation
└── Interpreting/
    └── TestBuiltIns.cs        # test(), describe(), assert.*, captureOutput()
```

The `test()` and `describe()` functions and the `assert` namespace are registered in `TestBuiltIns.cs`, following the same pattern as other [built-in function registries](../Stash%20—%20Standard%20Library%20Reference.md#overview).

### Integration Points

- **CLI (`Program.cs`)** — The `--test` flag instantiates a `TapReporter` and assigns it to `Interpreter.TestHarness` before script execution.
- **Interpreter** — Exposes `ITestHarness? TestHarness { get; set; }`. All harness calls are guarded: `TestHarness?.OnTestStart(...)`.
- **`test()` built-in** — Catches `RuntimeError` and `AssertionException` inside the test lambda and routes them to `OnTestFail`. All other exceptions propagate normally.
- **`assert.*` functions** — In test mode, throw `AssertionException` (caught by the `test()` wrapper). Outside test mode, throw `RuntimeError` (crashes the script).

---

## 9. Future Extensions

- **`test.skip(name, fn)`** — Skip a test with a reason; emits `ok N # SKIP` in TAP output.
- **`test.only(name, fn)`** — Run only this test (for focused debugging); all other tests are skipped.
- **`assert.deepEqual(a, b)`** — Recursive equality for arrays, dicts, and struct instances.
- **`assert.closeTo(a, b, delta)`** — Float comparison with tolerance (avoids floating-point precision issues).
- **`--test-format=tap|json|junit`** — Alternate output formats for different CI/CD consumers.
- **Test discovery** — `stash --test tests/` discovers and runs all `*_test.stash` or `test_*.stash` files automatically.
- **Setup/teardown** — `beforeEach()`, `afterEach()`, `beforeAll()`, `afterAll()` lifecycle hooks scoped to `describe()` blocks.
- **IDE integration** — VS Code test explorer adapter following the [DAP pattern](DAP%20—%20Debug%20Adapter%20Protocol.md), enabling inline pass/fail indicators and test run controls.

### Complete Example

```stash
#!/usr/bin/env stash

import { deploy, Server } from "deploy.stash";

describe("deployment", () => {
    test("creates server instance", () => {
        let srv = Server { host: "10.0.0.1", port: 22, status: "unknown" };
        assert.equal(srv.host, "10.0.0.1");
        assert.equal(srv.port, 22);
    });

    test("deploy returns boolean", () => {
        let srv = Server { host: "localhost", port: 22, status: "unknown" };
        let result = deploy(srv, "app.tar.gz");
        assert.equal(typeof(result), "bool");
    });

    test("null coalescing works", () => {
        let val = null ?? "default";
        assert.equal(val, "default");
    });

    test("type coercion does not happen", () => {
        assert.notEqual(5, "5");
        assert.notEqual(0, false);
        assert.notEqual(0, null);
    });
});
```

Running: `stash --test deploy_test.stash`

---

_This is a living document. Update as the testing infrastructure evolves._
