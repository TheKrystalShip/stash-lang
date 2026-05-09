# TAP Testing Framework Guidelines

Stash includes a built-in TAP v14 test framework. Tests are regular Stash function calls (`test.it()`, `test.describe()`, `test.skip()`) — not special AST nodes. The harness attaches at VM startup via `--test` CLI flag. See `docs/TAP — Testing Infrastructure.md` for the full spec.

## Architecture

```
Stash.Tap/
└── TapReporter.cs           → TAP v14 output formatter with YAML diagnostics
Stash.Core/Runtime/
├── ITestHarness.cs          → Interface: lifecycle callbacks (start/pass/fail/skip/discover)
└── AssertionError.cs        → Structured assertion failure (expected/actual fields)
Stash.Stdlib/BuiltIns/
└── TestBuiltIns.cs          → Registers test.it(), test.describe(), test.skip(), assert.*, hooks, captureOutput()
```

**CLI integration** (`Stash.Cli/Program.cs`):

- `--test` → activates test mode (creates `TapReporter`, sets `interpreter.TestHarness`)
- `--test-filter=pattern1;pattern2` → semicolon-separated prefix-match filters
- `--test-list` → discovery mode (emits `# discovered:` comments, doesn't execute test bodies)

## ITestHarness Interface

```csharp
public interface ITestHarness
{
    void OnTestStart(string name, SourceSpan span);
    void OnTestPass(string name, TimeSpan duration);
    void OnTestFail(string name, string message, SourceSpan? span, TimeSpan duration);
    void OnTestSkip(string name, string? reason);
    void OnSuiteStart(string name);
    void OnSuiteEnd(string name, int passed, int failed, int skipped);
    void OnRunComplete(int passed, int failed, int skipped);
    void OnTestDiscovered(string name, SourceSpan span);
    int PassedCount { get; }
    int FailedCount { get; }
    int SkippedCount { get; }
}
```

Zero overhead when not testing — `TestHarness` is null-checked before every call.

## Test Functions (TestBuiltIns.cs)

All registered as `BuiltInFunction` delegates on the interpreter:

| Function             | Behavior                                                                                                                           |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| `test(name, fn)`     | Runs beforeEach hooks → fn() → afterEach hooks. Catches `AssertionError`/`RuntimeError`, reports to harness. Continues on failure. |
| `skip(name, fn)`     | Registers as skipped, body never executes. TAP output: `ok N - name # SKIP`                                                        |
| `describe(name, fn)` | Groups tests hierarchically. Pushes hook layers, executes body, runs afterAll in `finally`, pops layers.                           |
| `beforeAll(fn)`      | Executes immediately (synchronous). Must be inside `describe`.                                                                     |
| `afterAll(fn)`       | Registered, runs when `describe` block ends (in `finally`).                                                                        |
| `beforeEach(fn)`     | Registered, runs before each `test()` in scope. Inherited from parent describes (outermost first).                                 |
| `afterEach(fn)`      | Registered, runs after each `test()` in scope. Inherited from parent describes (innermost first).                                  |
| `captureOutput(fn)`  | Redirects `interp.Output` to `StringWriter`, executes fn(), returns captured string, restores output in `finally`.                 |

### Assert Namespace

| Function                            | Behavior                                                                         |
| ----------------------------------- | -------------------------------------------------------------------------------- |
| `assert.equal(actual, expected)`    | Strict equality, no type coercion. Throws `AssertionError` with expected/actual. |
| `assert.notEqual(actual, expected)` | Strict inequality                                                                |
| `assert.true(value)`                | Truthiness check                                                                 |
| `assert.false(value)`               | Falsiness check                                                                  |
| `assert.null(value)`                | Null check                                                                       |
| `assert.notNull(value)`             | Non-null check                                                                   |
| `assert.greater(a, b)`              | Numeric comparison (converts to double)                                          |
| `assert.less(a, b)`                 | Numeric comparison                                                               |
| `assert.throws(fn)`                 | Returns error message string if fn() throws, otherwise throws AssertionError     |
| `assert.fail(message?)`             | Unconditional failure                                                            |

## Interpreter State

Hook stacks use `List<List<IStashCallable>>` — one layer per `describe` nesting level:

```csharp
// Pushed/popped by describe()
internal List<List<IStashCallable>> BeforeEachHooks;
internal List<List<IStashCallable>> AfterEachHooks;
internal List<List<IStashCallable>> AfterAllHooks;
```

Test naming builds fully qualified names: `filename > describe > nested describe > test name`

## Test Filtering

`interpreter.TestFilter` is `string[]?` (split from `--test-filter`). Matching is **prefix-based** — filter `"test.stash > math"` runs all tests in the "math" describe block. Multiple patterns are OR'd.

## Discovery Mode

When `interpreter.DiscoveryMode = true`, `test()` calls `harness.OnTestDiscovered()` instead of running the body. `describe()` blocks still execute (to discover nested tests). TAP output: `# discovered: fullName [file:line:col]`

## VS Code Integration

The VS Code extension (`src/testing.ts`, `src/testDiscovery.ts`, `src/tapParser.ts`) consumes TAP output:

- **Static discovery:** regex-based parsing of `test()`/`describe()`/`skip()` calls with brace-depth tracking
- **Dynamic discovery:** spawns `stash --test --test-list` and parses `# discovered:` comments
- **TAP parser:** state machine parsing `ok`/`not ok` lines with YAML diagnostic blocks
- **Test item IDs:** `>` separator — `file.test.stash > describe > test name`

## Tests

- `Stash.Tests/Interpreting/TestBuiltInsTests.cs` — assert functions, test/describe/skip execution, lifecycle hooks, TAP output format, captureOutput
- `Stash.Tests/Interpreting/TestDiscoveryTests.cs` — discovery mode, plan lines, filter interaction
- `Stash.Tests/Interpreting/TestFilterTests.cs` — exact match, prefix match, multiple patterns, describe-level filtering
