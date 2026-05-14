# TAP - Testing Infrastructure

> **Status:** Stable v1 testing reference
> **Audience:** test authors, CI maintainers, tool authors, and implementers
> **Purpose:** reference for Stash's built-in `test` and `assert` namespaces, TAP output, and CLI test mode.

Stash tests are ordinary Stash programs. Test cases are declared with the `test`
namespace, checked with the `assert` namespace, and run with the CLI test harness.
When the harness is active, failures are collected and reported as TAP instead of
stopping the entire script at the first assertion failure.

**Companion documents:**

- [Language Specification](Stash%20%E2%80%94%20Language%20Specification.md) - language semantics used by test code
- [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md) - generated `test` and `assert` API reference
- [DAP - Debug Adapter Protocol](DAP%20%E2%80%94%20Debug%20Adapter%20Protocol.md) - debugger behavior
- [LSP - Language Server Protocol](LSP%20%E2%80%94%20Language%20Server%20Protocol.md) - diagnostics and editor integration

---

## Contents

1. [Overview](#overview)
2. [Running Tests](#running-tests)
3. [Writing Tests](#writing-tests)
4. [Assertions](#assertions)
5. [Lifecycle Hooks](#lifecycle-hooks)
6. [Output Capture](#output-capture)
7. [TAP Output](#tap-output)
8. [Failures and Exit Behavior](#failures-and-exit-behavior)
9. [Patterns](#patterns)
10. [Tooling Contract](#tooling-contract)

---

## Overview

The testing API has two namespaces:

| Namespace | Purpose                                                                           |
| --------- | --------------------------------------------------------------------------------- |
| `test`    | declares tests, suites, hooks, exclusive tests, skipped tests, and output capture |
| `assert`  | raises assertion failures with structured messages and source locations           |

Basic example:

```stash
test.describe("math", () => {
    test.it("adds numbers", () => {
        assert.equal(1 + 1, 2);
    });

    test.it("keeps types distinct", () => {
        assert.notEqual(5, "5");
    });
});
```

Run it with:

```bash
stash --test math_test.stash
```

Assertions are usable outside test mode too. Without a test harness, assertion
failures behave like ordinary runtime errors and stop the script.

## Running Tests

### CLI Modes

| Command                                     | Behavior                                             |
| ------------------------------------------- | ---------------------------------------------------- |
| `stash --test file.stash`                   | run a test file and emit TAP                         |
| `stash --test-list file.stash`              | discover and list tests without running bodies       |
| `stash --test-filter=<patterns> file.stash` | run/discover tests whose full names match the filter |

`--test-list` implies `--test`. Test mode requires a script file. It cannot be used
with `-c`, stdin execution, `--compile`, `--debug`, or `--disassemble`.

Test filters are semicolon-separated name prefixes.

```bash
stash --test-filter="math > adds;strings" tests.stash
```

A filtered-out test emits no TAP result. A filtered-out `describe` block is skipped
entirely when none of its descendant names can match.

### Test Names

Every test has a full hierarchical name.

```stash
test.describe("parser", () => {
    test.it("accepts literals", () => {});
});
```

The full name includes the file name and describe path:

```text
parser_test.stash > parser > accepts literals
```

Nested `test.describe` blocks append with `>`.

## Writing Tests

### `test.it(name, fn)`

Defines and executes a test case.

```stash
test.it("loads config", () => {
    let cfg = config.parse("port = 8080", "toml");
    assert.equal(cfg.port, 8080);
});
```

In test mode, runtime errors and assertion failures inside the body are reported as
test failures. Execution continues to later tests.

### `test.describe(name, fn)`

Groups tests under a suite name.

```stash
test.describe("deployment", () => {
    test.it("validates target", () => {
        assert.true(validate("prod"));
    });
});
```

Suites may be nested. Hook scope follows the describe nesting.

### `test.skip(name, fn)`

Registers a skipped test without running its body.

```stash
test.skip("rollback is not implemented", () => {
    assert.fail("body is not executed");
});
```

Skipped tests emit an `ok` TAP line with a `# SKIP` directive.

### `test.only(name, fn)`

Defines an exclusive test. When one or more `test.only` calls are present,
ordinary `test.it` tests are skipped with reason `test.only active`; `test.only`
tests still run.

```stash
test.only("current failure", () => {
    assert.deepEqual(build(), expected);
});
```

Use `test.only` for local debugging. It should not be committed in normal test
suites unless the intent is deliberately narrow.

## Assertions

Assertion functions throw `AssertionError` on failed expectations.

| Function                                  | Contract                                   |
| ----------------------------------------- | ------------------------------------------ |
| `assert.equal(actual, expected)`          | strict equality, no type coercion          |
| `assert.notEqual(actual, expected)`       | strict inequality                          |
| `assert.true(value)`                      | value is truthy                            |
| `assert.false(value)`                     | value is falsey                            |
| `assert.null(value)`                      | value is `null`                            |
| `assert.notNull(value)`                   | value is not `null`                        |
| `assert.greater(a, b)`                    | numeric `a > b`                            |
| `assert.less(a, b)`                       | numeric `a < b`                            |
| `assert.throws(fn)`                       | callable throws; returns the error message |
| `assert.fail(message?)`                   | immediately fails                          |
| `assert.deepEqual(actual, expected)`      | recursive structural equality              |
| `assert.closeTo(actual, expected, delta)` | numeric tolerance check                    |

### Equality

`assert.equal` uses Stash equality rules and does not coerce unrelated types.

```stash
assert.equal(5, 5);
assert.notEqual(5, "5");
assert.notEqual(0, false);
```

Use `assert.deepEqual` for arrays, dictionaries, and structs when structural
comparison is intended.

```stash
assert.deepEqual([1, [2, 3]], [1, [2, 3]]);
assert.deepEqual({ host: "localhost", port: 5432 },
                 { host: "localhost", port: 5432 });
```

On failure, `assert.deepEqual` reports the failing path when available.

### Numeric Assertions

`assert.greater`, `assert.less`, and `assert.closeTo` require numeric arguments.
Passing non-numeric values produces a runtime type error.

```stash
assert.greater(10, 5);
assert.less(3, 4);
assert.closeTo(3.14159, 3.14, 0.01);
```

`assert.closeTo` requires a non-negative `delta`.

### Error Assertions

`assert.throws(fn)` succeeds when `fn` throws a runtime error and returns the error
message.

```stash
let msg = assert.throws(() => {
    throw ValueError { message: "bad input" };
});
assert.true(str.contains(msg, "bad input"));
```

If the function does not throw, `assert.throws` fails.

## Lifecycle Hooks

Hooks must be registered inside `test.describe`.

| Hook                  | Timing                                                         |
| --------------------- | -------------------------------------------------------------- |
| `test.beforeAll(fn)`  | executes immediately when encountered inside the describe body |
| `test.afterAll(fn)`   | runs when the current describe block exits                     |
| `test.beforeEach(fn)` | runs before each test in the current describe scope            |
| `test.afterEach(fn)`  | runs after each test in the current describe scope             |

```stash
test.describe("counter", () => {
    let state = [0];

    test.beforeEach(() => {
        state[0] = 0;
    });

    test.afterEach(() => {
        state[0] = -1;
    });

    test.it("starts clean", () => {
        assert.equal(state[0], 0);
    });
});
```

Using a hook outside `test.describe` produces a runtime error.

### Nested Hook Order

`beforeEach` hooks run outermost to innermost. `afterEach` hooks run innermost to
outermost.

```stash
test.describe("outer", () => {
    test.beforeEach(() => io.println("outer setup"));
    test.afterEach(() => io.println("outer cleanup"));

    test.describe("inner", () => {
        test.beforeEach(() => io.println("inner setup"));
        test.afterEach(() => io.println("inner cleanup"));

        test.it("example", () => {});
    });
});
```

Order for the test body:

1. outer `beforeEach`
2. inner `beforeEach`
3. test body
4. inner `afterEach`
5. outer `afterEach`

`afterAll` hooks run in the `finally` path of their describe block.

## Output Capture

`test.captureOutput(fn)` redirects Stash output to an in-memory buffer while `fn`
runs, restores the previous output writer, and returns the captured string.

```stash
let output = test.captureOutput(() => {
    io.println("hello");
    io.print("world");
});

assert.equal(output, "hello\nworld");
```

Captured output does not appear in TAP output. Nested output capture is supported:
the inner capture temporarily replaces the outer capture and then restores it.

## TAP Output

Stash emits TAP version 14.

```text
TAP version 14
# math_test.stash > math
ok 1 - math_test.stash > math > adds numbers
not ok 2 - math_test.stash > math > string equality
  ---
  message: "assert.equal failed: expected \"hello\" but got \"world\""
  severity: fail
  at:
    file: math_test.stash
    line: 14
    column: 5
  ...
ok 3 - math_test.stash > math > skipped case # SKIP skipped
1..3
```

Output rules:

- the first test or suite writes `TAP version 14`
- suites are emitted as TAP comments beginning with `#`
- passing tests emit `ok N - <name>`
- failing tests emit `not ok N - <name>` followed by a YAML diagnostic block
- skipped tests emit `ok N - <name> # SKIP <reason>`
- the plan line `1..N` is emitted when the run completes

`--test-list` uses TAP comments instead of result lines.

```text
TAP version 14
# discovered: math_test.stash > math > adds numbers [math_test.stash:3:1]
1..0
```

## Failures and Exit Behavior

Inside `test.it` or `test.only`, assertion failures and runtime errors are converted
to failed TAP tests when a test harness is active. Later tests continue to run.

Outside a test case, runtime errors behave normally and may abort the script.
Failures in `test.describe` setup code before a test starts are not reported as a
test failure unless they occur inside a test body or hook executed as part of a test.

Assertions outside test mode throw normally.

```stash
assert.equal(1, 2); // crashes a normal script
```

The process exit code is controlled by the CLI test runner. A run with any failed
tests exits non-zero. A run with only passing and skipped tests exits zero.

## Patterns

### Complete Example

```stash
#!/usr/bin/env stash

test.describe("deployment", () => {
    let srv = null;

    test.beforeEach(() => {
        srv = { host: "localhost", port: 22 };
    });

    test.it("creates server fixture", () => {
        assert.equal(srv.host, "localhost");
        assert.equal(srv.port, 22);
    });

    test.it("captures output", () => {
        let output = test.captureOutput(() => {
            io.println("deploying ${srv.host}");
        });
        assert.equal(output, "deploying localhost\n");
    });

    test.skip("rollback not implemented", () => {
        assert.fail("not reached");
    });
});
```

### Testing Errors

```stash
test.it("rejects invalid input", () => {
    let msg = assert.throws(() => validatePort(70000));
    assert.true(str.contains(msg, "port"));
});
```

### Testing Floating Point Results

```stash
test.it("computes ratio", () => {
    assert.closeTo(computeRatio(), 0.3333, 0.001);
});
```

### Focused Debugging

```stash
test.only("failing case", () => {
    assert.deepEqual(actual(), expected());
});
```

Remove `test.only` before committing broad test suites.

## Tooling Contract

Tooling may rely on these stable surfaces:

- test execution is requested with `stash --test <file>`
- discovery is requested with `stash --test-list <file>`
- filtering uses `--test-filter=<semicolon-separated-prefixes>`
- TAP output is version 14
- test names are hierarchical strings separated by `>`
- failures include message, severity, and source location when available
- `test` and `assert` API signatures are defined in the
  [Standard Library Reference](Stash%20%E2%80%94%20Standard%20Library%20Reference.md)

Future alternate output formats, if added, should preserve the same logical test
events: discovered, suite start, test start, pass, fail, skip, and run complete.
