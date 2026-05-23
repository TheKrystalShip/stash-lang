# DAP — Inline Value Reporting

**Status:** Draft
**Created:** 2025-07-10
**Scope:** VS Code Extension (`stash-lang`)

## 1. Purpose

When a user steps through Stash code in the debugger, variable values should appear inline at the end of each line in the editor — the same "inline values" behavior users know from JavaScript, Python, and C# debugging in VS Code.

**Before:** User must hover over variables or expand the Variables pane to see values.
**After:** Variables display their current values inline, directly in the editor, as faded text at the end of each line.

## 2. Architecture Decision

### The Mechanism: VS Code `InlineValuesProvider` API

This feature is **entirely a VS Code extension-side concern**, not a DAP protocol feature. Despite the DAP specification including a `supportsInlineValues` capability flag, VS Code does **not** use the DAP-level `InlineValue` request. Instead, VS Code exposes a TypeScript API — `languages.registerInlineValuesProvider()` — that extensions implement.

**Decision:** Implement an `InlineValuesProvider` in the VS Code extension TypeScript code. No changes to the C# DAP server are required.

**Rationale:**

- The Stash DAP server already supports the `evaluate` request with frame-scoped context (`DebugSession.Evaluate()`), which is what VS Code uses under the hood to resolve variable values
- The `InlineValuesProvider` API is the standard integration point — it's what the built-in JS/TS debugger, Python, C#, and Go extensions all use
- Keeping this extension-side avoids adding a new request handler to the C# DAP server for something VS Code won't call anyway

**Alternatives rejected:**

- _DAP-level `InlineValue` request_ — OmniSharp's DAP library (v0.19.9) doesn't appear to have first-class support for this request. Even if it did, VS Code uses the extension API, not the DAP request. Adding it server-side would be dead code.
- _Text decorations via extension API_ — Manually rendering decorations would duplicate what VS Code already provides when an `InlineValuesProvider` is registered. More code, worse UX (no theme integration, no VS Code setting respect), and fragile.

## 3. VS Code API Surface

### 3.1 Registration

```typescript
languages.registerInlineValuesProvider(
  { language: "stash", scheme: "file" },
  new StashInlineValuesProvider(),
);
```

Registered during `activate()` in `extension.ts`, pushed to `context.subscriptions`.

### 3.2 Provider Interface

```typescript
interface InlineValuesProvider {
  onDidChangeInlineValues?: Event<void>;
  provideInlineValues(
    document: TextDocument,
    viewPort: Range, // visible editor range
    context: InlineValueContext,
    token: CancellationToken,
  ): ProviderResult<InlineValue[]>;
}
```

The `InlineValueContext` provides:

- `frameId: number` — the active stack frame ID (used by VS Code to scope DAP evaluate calls)
- `stoppedLocation: Range` — the range/line where execution is currently paused

### 3.3 Return Types

VS Code supports three kinds of inline values:

| Type                                                              | What it does                                                             | When to use                                                      |
| ----------------------------------------------------------------- | ------------------------------------------------------------------------ | ---------------------------------------------------------------- |
| `InlineValueVariableLookup(range, variableName?, caseSensitive?)` | VS Code calls DAP `evaluate` with the variable name, scoped to the frame | **Default choice** — simplest, leverages existing infrastructure |
| `InlineValueEvaluatableExpression(range, expression?)`            | VS Code calls DAP `evaluate` with an arbitrary expression                | For computed expressions like `arr.len()`                        |
| `InlineValueText(range, text)`                                    | Extension provides the text directly                                     | When value is already known                                      |

**Decision:** Use `InlineValueVariableLookup` for all variable references. This is the lightest-weight option: the extension identifies variable locations and names in the source, and VS Code handles evaluation via the existing DAP `evaluate` request.

## 4. Implementation Design

### 4.1 New File: `src/inlineValues.ts`

A single new TypeScript file in the extension:

```typescript
import * as vscode from "vscode";

// Stash keywords that should never be treated as variable names
const KEYWORDS = new Set([
  "let",
  "const",
  "fn",
  "return",
  "if",
  "else",
  "for",
  "in",
  "while",
  "break",
  "continue",
  "true",
  "false",
  "null",
  "struct",
  "enum",
  "interface",
  "import",
  "from",
  "as",
  "try",
  "catch",
  "finally",
  "throw",
  "switch",
  "case",
  "default",
  "match",
  "typeof",
  "delete",
  "spawn",
  "await",
  "elevate",
  "extend",
  "and",
  "or",
  "not",
  "retry",
  "new",
]);

// Regex to find identifiers in a line of Stash code
// Matches word-boundary identifiers that start with a letter or underscore
const IDENTIFIER_RE = /\b([a-zA-Z_]\w*)\b/g;

export class StashInlineValuesProvider implements vscode.InlineValuesProvider {
  provideInlineValues(
    document: vscode.TextDocument,
    viewPort: vscode.Range,
    context: vscode.InlineValueContext,
    _token: vscode.CancellationToken,
  ): vscode.InlineValue[] {
    const result: vscode.InlineValue[] = [];
    const seen = new Set<string>(); // de-duplicate per line

    // Only show values from the viewport start up to (and including)
    // the line where execution stopped — lines after the stop point
    // haven't executed yet, so their values would be stale/misleading
    const endLine = Math.min(
      context.stoppedLocation.start.line,
      viewPort.end.line,
    );
    const startLine = viewPort.start.line;

    for (let line = startLine; line <= endLine; line++) {
      const text = document.lineAt(line).text;
      seen.clear();

      // Skip comment-only lines
      const trimmed = text.trimStart();
      if (trimmed.startsWith("//") || trimmed.startsWith("#")) {
        continue;
      }

      // Strip inline comments and string literals to avoid false matches
      const cleaned = stripStringsAndComments(text);

      let match: RegExpExecArray | null;
      IDENTIFIER_RE.lastIndex = 0;
      while ((match = IDENTIFIER_RE.exec(cleaned)) !== null) {
        const name = match[1];
        if (KEYWORDS.has(name) || seen.has(name)) continue;

        // Skip identifiers that look like function calls (followed by '(')
        const afterIdx = match.index + name.length;
        if (afterIdx < cleaned.length && cleaned[afterIdx] === "(") continue;

        // Skip identifiers that look like property access (preceded by '.')
        if (match.index > 0 && cleaned[match.index - 1] === ".") continue;

        // Skip identifiers immediately after 'fn ' (function declarations)
        if (
          match.index >= 3 &&
          cleaned.substring(match.index - 3, match.index) === "fn "
        )
          continue;

        seen.add(name);

        const range = new vscode.Range(
          line,
          match.index,
          line,
          match.index + name.length,
        );
        result.push(new vscode.InlineValueVariableLookup(range, name, true));
      }
    }

    return result;
  }
}
```

### 4.2 `stripStringsAndComments` Helper

To avoid matching identifiers inside string literals or comments:

```typescript
function stripStringsAndComments(line: string): string {
  // Replace string contents with spaces (preserve offsets for correct Range mapping)
  let result = "";
  let i = 0;
  while (i < line.length) {
    // Single-line comment
    if (line[i] === "/" && i + 1 < line.length && line[i + 1] === "/") {
      result += " ".repeat(line.length - i);
      break;
    }
    // Hash comment
    if (line[i] === "#") {
      result += " ".repeat(line.length - i);
      break;
    }
    // String literal (double or single quote)
    if (line[i] === '"' || line[i] === "'") {
      const quote = line[i];
      result += " "; // replace opening quote
      i++;
      while (i < line.length && line[i] !== quote) {
        if (line[i] === "\\" && i + 1 < line.length) {
          result += "  "; // escaped char
          i += 2;
        } else {
          result += " ";
          i++;
        }
      }
      if (i < line.length) {
        result += " "; // closing quote
        i++;
      }
      continue;
    }
    // Command substitution $(...) — preserve dollar but blank out parens content
    // (variable interpolation inside commands shouldn't be matched)
    result += line[i];
    i++;
  }
  return result;
}
```

### 4.3 Registration in `extension.ts`

```typescript
import { StashInlineValuesProvider } from "./inlineValues";

// Inside activate():
context.subscriptions.push(
  vscode.languages.registerInlineValuesProvider(
    { language: "stash", scheme: "file" },
    new StashInlineValuesProvider(),
  ),
);
```

## 5. Variable Detection Strategy

### 5.1 What Gets Shown

The provider identifies identifiers in the source text and returns them as variable lookups. VS Code then calls DAP `evaluate` for each one. If evaluation succeeds, the value is displayed; if it fails (e.g., the identifier is a function name, a namespace, or out of scope), VS Code silently skips it.

**Shown:**

- Local variables: `let x = 5` → shows `x = 5`
- Constants: `const name = "hello"` → shows `name = "hello"`
- Function parameters: `fn greet(name, age)` → shows `name` and `age` values
- Loop variables: `for item in list` → shows `item` value
- Reassigned variables: `x = x + 1` → shows updated `x`

**Not shown (filtered out):**

- Keywords (`let`, `if`, `for`, `return`, etc.)
- Function names in declarations (`fn myFunc(...)` — `myFunc` is filtered)
- Property accesses (`obj.field` — `field` after `.` is filtered, but `obj` is shown)
- String literal contents
- Comments
- Identifiers on lines after the current stop line

### 5.2 Keyword List Maintenance

The keyword set must stay in sync with the Stash language. If new keywords are added to the lexer, they should be added to the `KEYWORDS` set. This is a low-frequency maintenance task — Stash rarely adds new keywords.

> **Risk:** If a keyword is missing from the set, VS Code will attempt to evaluate it via DAP. The evaluate will fail (keywords aren't in scope), and VS Code will silently skip it. The impact is a wasted DAP roundtrip, not a user-visible bug. Acceptable.

### 5.3 Why Not Use the LSP for Semantic Analysis?

**Considered:** Ask the LSP for semantic tokens to precisely identify variable references vs. function names vs. types.

**Rejected:** The inline values provider is called synchronously during debug stops. Making an LSP request would introduce latency and complexity (cross-server communication, race conditions). The regex approach is fast, simple, and the false-positive cost is minimal (a failed DAP evaluate is invisible to the user).

**Revisit trigger:** If users report excessive flickering or slow inline value display, consider caching or LSP integration.

## 6. Behavior Specification

### 6.1 When Values Appear

- Values appear **only while the debugger is stopped** (breakpoint, step, pause)
- Values appear for all lines **from the top of the viewport down to the stopped line**
- Lines **below** the stopped line show no values (haven't executed yet)
- Values **disappear immediately** when execution resumes (continue, step)

### 6.2 Value Formatting

The DAP server's existing `Evaluate()` method returns a `value` string. The existing `FormatVariable()` method in `DebugSession.cs` already produces display-ready strings:

| Type              | Display Format    | Example      |
| ----------------- | ----------------- | ------------ |
| `int`             | Plain number      | `42`         |
| `float`           | Invariant culture | `3.14`       |
| `bool`            | Lowercase         | `true`       |
| `string`          | Quoted            | `"hello"`    |
| `null`            | Literal           | `null`       |
| `array`           | Count             | `array[3]`   |
| `dict`            | Count             | `dict[5]`    |
| `struct instance` | Type name         | `User {...}` |
| `function`        | Name              | `fn greet`   |
| `enum`            | Qualified         | `Color.Red`  |

No changes to `FormatVariable()` are needed — the existing formatting is suitable for inline display.

### 6.3 User Control

VS Code has a built-in setting `debug.inlineValues` that controls inline value display:

- `"on"` — always show (when provider is registered)
- `"off"` — never show
- `"auto"` — show when a provider is registered (default)

No Stash-specific setting is needed. The built-in toggle is sufficient.

### 6.4 Edge Cases

| Scenario                                               | Behavior                                                                                         |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| Variable shadowing (same name in nested scope)         | VS Code evaluates in the current frame's scope — shows the innermost binding, which is correct   |
| Variable declared but not yet assigned on current line | DAP evaluate returns whatever the current scope holds; if not yet bound, evaluate fails silently |
| Very long values (large strings, big arrays)           | `FormatVariable()` already truncates display (e.g., `array[100]` not the full contents)          |
| Multiple threads                                       | `InlineValueContext.frameId` scopes to the active thread's frame — correct behavior              |
| No debug session active                                | Provider is never called — VS Code only invokes it during debug stops                            |
| Binary/minified files                                  | Regex still works; results may be noisy but the file isn't a normal editing target               |

## 7. Testing Strategy

### 7.1 Manual Testing

Since this is a VS Code extension UI feature, primary testing is manual:

1. **Basic:** Set breakpoint in a simple script, step through, verify variables appear inline
2. **Scope correctness:** Verify local vs. closure vs. global variables show correct values
3. **Function parameters:** Step into a function, verify parameters display
4. **Loop variables:** Step through a `for` loop, verify iterator variable updates
5. **String filtering:** Variables inside string literals should NOT appear as inline values
6. **Comment filtering:** Variables inside comments should NOT appear
7. **Property access:** `obj.field` should show `obj`'s value, not try to evaluate `field` alone
8. **Keywords:** `let`, `if`, `for` etc. should never appear as inline values
9. **Performance:** Open a large file (500+ lines), set breakpoint at the end, verify no visible lag

### 7.2 Unit Tests for `stripStringsAndComments`

The string/comment stripping logic can be unit-tested in isolation:

```typescript
// Test cases:
stripStringsAndComments('let x = "hello world"'); // 'let x =                 '
stripStringsAndComments("let x = 5 // comment"); // 'let x = 5              '
stripStringsAndComments('let x = "escaped \\" quote"'); // handles escapes
stripStringsAndComments("let x = 'single'"); // strips single quotes
stripStringsAndComments("# full line comment"); // all spaces
```

### 7.3 Unit Tests for `StashInlineValuesProvider`

Test the provider's identifier extraction logic with mock `TextDocument`s:

```typescript
// Returns InlineValueVariableLookup for 'x' and 'y', not 'let'
provideInlineValues(doc('let x = y + 1'), ...)

// Filters out function names after 'fn'
provideInlineValues(doc('fn greet(name) {'), ...)  // returns 'name', not 'greet'

// Filters out property accesses
provideInlineValues(doc('x = obj.field'), ...)  // returns 'x', 'obj', not 'field'

// Respects stopped location — no values after stop line
provideInlineValues(doc(...), viewport(0,10), context(stoppedLine=5), ...)
// only returns values for lines 0-5
```

## 8. Implementation Checklist

- [ ] Create `src/inlineValues.ts` with `StashInlineValuesProvider` class
- [ ] Implement `stripStringsAndComments()` helper
- [ ] Implement `provideInlineValues()` with identifier regex, keyword filtering, and comment/string stripping
- [ ] Register the provider in `extension.ts` `activate()` function
- [ ] Add unit tests for `stripStringsAndComments`
- [ ] Add unit tests for `provideInlineValues` identifier extraction
- [ ] Manual testing with breakpoints, stepping, various code patterns
- [ ] Verify `KEYWORDS` set is complete against current lexer
- [ ] Build and package the extension

## 9. Impact Analysis

### What Changes

| Component                                           | Change                                 | Scope     |
| --------------------------------------------------- | -------------------------------------- | --------- |
| `.vscode/extensions/stash-lang/src/inlineValues.ts` | **New file** — provider implementation | ~80 lines |
| `.vscode/extensions/stash-lang/src/extension.ts`    | **One line** — register provider       | Minimal   |
| **Stash.Dap/**                                      | **No changes**                         | —         |
| **Stash.Lsp/**                                      | **No changes**                         | —         |
| **Stash.Core/**, **Stash.Interpreter/**             | **No changes**                         | —         |

### Risks

| Risk                                                                    | Likelihood | Impact                              | Mitigation                                                                                                  |
| ----------------------------------------------------------------------- | ---------- | ----------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Missing keywords cause wasted evaluate calls                            | Low        | Negligible — silent failure         | Periodic audit against lexer                                                                                |
| Regex falsely matches inside template literals or command substitutions | Medium     | Minor — wrong variable shown inline | `stripStringsAndComments` handles standard cases; template literals (`${...}` inside strings) are edge case |
| Performance with very large viewports                                   | Low        | Minor delay in value display        | VS Code only calls provider for visible viewport; regex scan is O(n) in line count                          |

## 10. Non-Goals

- **Expression evaluation beyond simple variables** — We don't try to show `arr[0]` or `dict["key"]` inline. Only simple variable names. Users can hover for complex expressions.
- **Custom rendering or rich formatting** — We use VS Code's built-in inline value display, not custom decorations.
- **DAP server changes** — No new handlers, capabilities, or protocol extensions.
- **Stash-specific settings** — VS Code's `debug.inlineValues` setting is sufficient.
