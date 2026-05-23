# Formatter Options — Configurability Analysis & Recommendations

**Status:** Backlog — Analysis Complete
**Created:** 2026-04-11
**Purpose:** Evaluate what additional formatting options Stash should offer, informed by how other language formatters balance simplicity vs. configurability.

---

## 0. Executive Summary

Stash currently has **6 formatting options**. After surveying 5 major formatters across the industry and auditing every hardcoded formatting decision in our formatter, the recommendation is to add **3 new options** (bringing the total to 9) and improve the behavior of 1 existing option. This keeps Stash firmly in the "opinionated with escape hatches" camp alongside Prettier and Deno, and far away from rustfmt's 60+ knob sprawl.

---

## 1. Industry Survey

### 1.1 The "Zero Config" Camp

| Formatter | Language | Style Options | Philosophy                                                                                                  |
| --------- | -------- | ------------- | ----------------------------------------------------------------------------------------------------------- |
| **gofmt** | Go       | **0**         | "Gofmt's style is nobody's favorite, yet gofmt is everyone's favorite." Total uniformity.                   |
| **Black** | Python   | **3**         | `--line-length`, `--skip-string-normalization`, `--skip-magic-trailing-comma`. Everything else is dictated. |

### 1.2 The "Handful of Options" Camp

| Formatter           | Language  | Style Options | Philosophy                                                                                                                                                                                                     |
| ------------------- | --------- | ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Deno fmt**        | JS/TS     | **6**         | `useTabs`, `indentWidth`, `lineWidth`, `semiColons`, `singleQuote`, `proseWrap`                                                                                                                                |
| **Prettier**        | JS/TS/CSS | **~15**       | Deliberately limited. Ships with an [Option Philosophy](https://prettier.io/docs/option-philosophy) doc: "options are a gateway drug... the more options, the further from the goal of consistent formatting." |
| **Stash (current)** | Stash     | **6**         | `indentSize`, `useTabs`, `printWidth`, `trailingComma`, `endOfLine`, `bracketSpacing`                                                                                                                          |

### 1.3 The "Exhaustive" Camp

| Formatter   | Language | Style Options       | Philosophy                                                                                                                                     |
| ----------- | -------- | ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| **rustfmt** | Rust     | **60+** (20 stable) | Every construct has its own width threshold, brace style, spacing rule. Most power, but teams still argue about which of the 60 knobs to turn. |

### 1.4 Key Insight

The most successful formatters converge on **6–15 style options**. Below 6, teams that genuinely need different conventions can't use the tool. Above 15, the combinatorial explosion of possible styles defeats the purpose of having a formatter. The sweet spot is **8–12 options** where each option controls a meaningful, frequently-debated stylistic choice.

---

## 2. Current Stash Formatter Audit

### 2.1 Current 6 Options

| Option           | Type | Default | Description                                                 |
| ---------------- | ---- | ------- | ----------------------------------------------------------- |
| `indentSize`     | int  | 2       | Spaces per indent level                                     |
| `useTabs`        | bool | false   | Tabs instead of spaces                                      |
| `printWidth`     | int  | 80      | Target line width for Wadler-Lindig doc printer             |
| `trailingComma`  | enum | `none`  | `none` or `all` — trailing commas in multi-line collections |
| `endOfLine`      | enum | `lf`    | `lf`, `crlf`, or `auto`                                     |
| `bracketSpacing` | bool | true    | Spaces inside `{}` in single-line dicts/structs             |

### 2.2 Hardcoded Decisions

The formatter makes ~20 hardcoded stylistic choices. Categorized by value of making them configurable:

#### Already-Good Hardcoded Choices (KEEP hardcoded)

| Decision                 | Current Behavior                 | Why Keep Hardcoded                                                                                             |
| ------------------------ | -------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| Brace placement          | K&R (same line)                  | Core language identity. Two brace styles fragments the ecosystem in half.                                      |
| Operator spacing         | Always `a + b`                   | Universal standard. Nobody wants `a+b`.                                                                        |
| Semicolons               | Always present                   | **Semicolons are mandatory** in Stash's grammar — no ASI. This is a parser constraint, not a formatter choice. |
| Unary operator spacing   | No space: `!x`, `-5`             | Universal standard.                                                                                            |
| Keyword-paren spacing    | `if (cond)` with space           | Matches C-family convention.                                                                                   |
| Dot access spacing       | No spaces: `obj.field`           | Universal standard.                                                                                            |
| Type hint spacing        | `name: type` (space after colon) | Matches TypeScript/Python convention.                                                                          |
| Trailing newline         | Exactly one at EOF               | Universal standard, enforced by editorconfig.                                                                  |
| Shebangs                 | Preserved, blank line after      | Correct POSIX behavior.                                                                                        |
| Format-ignore directives | `// stash-ignore format`         | Already configurable via comment pragmas.                                                                      |
| `else` placement         | `} else {` on same line          | Part of K&R.                                                                                                   |

#### Potentially-Configurable Decisions (EVALUATE below)

| Decision                         | Current Behavior        | What Would Change                       |
| -------------------------------- | ----------------------- | --------------------------------------- |
| Collection breaking heuristic    | `≥3` items → multi-line | Could be driven by `printWidth` alone   |
| Blank lines between declarations | 1 blank line            | Could allow 0 or 2                      |
| Import sorting                   | Preserved order         | Could auto-sort alphabetically          |
| Single-line functions            | Never collapsed         | Could allow `fn f() { 42 }`             |
| Quote normalization              | None (only `"` exists)  | N/A — Stash only supports double quotes |
| Arrow function parens            | Always: `(x) =>`        | Could allow `x =>` for single params    |

---

## 3. Recommendations

### 3.1 Options to ADD (3 new options)

#### 3.1.1 `sortImports` — Auto-sort import statements

| Property         | Value                |
| ---------------- | -------------------- |
| **Type**         | `bool`               |
| **Default**      | `false`              |
| **Config key**   | `sortImports`        |
| **EditorConfig** | `stash_sort_imports` |

**What it does:** When `true`, sorts `import` statements alphabetically within each contiguous group (groups are separated by blank lines or non-import statements). Does NOT merge or split imports.

**Before (unsorted):**

```stash
import { readFile } from "fs";
import { parse } from "json";
import { join } from "path";
import { encrypt } from "crypto";
```

**After (sorted):**

```stash
import { encrypt } from "crypto";
import { readFile } from "fs";
import { parse } from "json";
import { join } from "path";
```

**Rationale:**

- Every major formatter ecosystem has import sorting (Black+isort, Prettier+eslint-plugin-import, rustfmt's `reorder_imports`, Deno fmt).
- Without it, teams either manually maintain sorted imports or accumulate entropy.
- Off by default: this changes semantics in edge cases (side-effect imports), so it must be opt-in.
- Sort key: the module path string (after `from`), case-insensitive.
- Destructured imports within a single statement should also be sorted: `import { a, b, c } from "foo"` (not `import { c, a, b }`).

**Alternatives considered:**

- Put this in `stash-check` as a lint rule → Rejected. Import sorting is a formatting concern, not a diagnostic. It's something `--write` should fix automatically.
- Sort by default → Rejected. Side-effect imports could break. Opt-in is safer.

**Risks:**

- Side-effect imports (if Stash ever adds them) could break when reordered. Mitigated by: off by default, and we can add a `// stash-ignore sort` comment to pin specific imports.
- Import groups (stdlib vs. user code) aren't distinguished. This is acceptable for V1; a future `importOrder` option could add group-aware sorting like Prettier's plugin.

---

#### 3.1.2 `blankLinesBetweenBlocks` — Blank lines between top-level declarations

| Property         | Value                              |
| ---------------- | ---------------------------------- |
| **Type**         | `int`                              |
| **Default**      | `1`                                |
| **Config key**   | `blankLinesBetweenBlocks`          |
| **EditorConfig** | `stash_blank_lines_between_blocks` |
| **Valid values** | `1` or `2`                         |

**What it does:** Controls how many blank lines appear between top-level declarations (functions, structs, enums, interfaces, extend blocks).

**With `blankLinesBetweenBlocks = 1` (default):**

```stash
fn foo() {
    // ...
}

fn bar() {
    // ...
}
```

**With `blankLinesBetweenBlocks = 2`:**

```stash
fn foo() {
    // ...
}


fn bar() {
    // ...
}
```

**Rationale:**

- PEP 8 (Python) uses 2 blank lines for module-level definitions. This is a common team preference.
- rustfmt has `blank_lines_upper_bound` for a similar purpose.
- Restricting to `1` or `2` prevents abuse (no `blankLinesBetweenBlocks = 10`).

**Alternatives considered:**

- `blank_lines_upper_bound` / `blank_lines_lower_bound` (rustfmt style) → Rejected. Too granular. Just one number for the common case.
- Make it 0/1/2 → Rejected. `0` puts declarations flush together, which hurts readability with no reasonable benefit.

**Risks:** Minimal. This only affects vertical whitespace between top-level blocks.

---

#### 3.1.3 `singleLineBlocks` — Allow single-line function/block bodies

| Property         | Value                      |
| ---------------- | -------------------------- |
| **Type**         | `bool`                     |
| **Default**      | `false`                    |
| **Config key**   | `singleLineBlocks`         |
| **EditorConfig** | `stash_single_line_blocks` |

**What it does:** When `true`, allows functions and control flow blocks with a single short statement to remain on one line, subject to `printWidth`.

**With `singleLineBlocks = true`:**

```stash
fn double(x) { return x * 2; }
fn greet(name) { io.println("Hello, " + name); }

if (done) { break; }
```

**With `singleLineBlocks = false` (default):**

```stash
fn double(x) {
    return x * 2;
}

fn greet(name) {
    io.println("Hello, " + name);
}

if (done) {
    break;
}
```

**Rationale:**

- rustfmt has `fn_single_line` and `empty_item_single_line` for this.
- Common request for utility functions, one-liner getters, and short guard clauses.
- Off by default: the expanded form is always more readable; this is an opt-in density preference.
- The formatter should only collapse to single-line if: (a) body is exactly 1 statement, (b) the entire line fits within `printWidth`, and (c) no comments inside the block.

**Alternatives considered:**

- Only for functions, not control flow → Considered, but inconsistent. If `fn f() { return 1; }` is ok, then `if (x) { break; }` should be too.
- Always collapse short bodies → Rejected. Expanding is the safer default for readability.

**Risks:**

- Interacts with `printWidth`: if printWidth is very large, long single-line functions could look bad. Mitigated by: the formatter can impose a reasonable maximum (e.g., half of printWidth) for single-line eligibility.
- AST preservation: the formatter must not collapse blocks with comments or multi-statements.

---

### 3.2 Existing Option to IMPROVE

#### 3.2.1 `printWidth` — Make it drive collection breaking

**Current behavior:** `printWidth` is passed to the Wadler-Lindig `DocPrinter` and affects group fitting. The formatter also uses a hardcoded heuristic: `≥3` items in a collection → always multi-line, regardless of printWidth.

**Proposed behavior:** Remove the `≥3` item count threshold. Let `printWidth` be the **sole** determinant: if a collection fits on one line within printWidth, keep it on one line; if not, break it vertically.

**Before (current — `arr` always multi-line despite fitting):**

```stash
// Even with printWidth=120, this array with 3 items is forced multi-line:
let colors = [
    "red",
    "green",
    "blue",
];
```

**After (proposed — printWidth drives the decision):**

```stash
// With printWidth=120, this fits, so it stays single-line:
let colors = ["red", "green", "blue"];

// With printWidth=40, it doesn't fit, so it breaks:
let colors = [
    "red",
    "green",
    "blue",
];
```

**Rationale:**

- This is how Prettier and Black work. The item count is irrelevant; what matters is whether the line fits.
- Users who set `printWidth = 120` expect wider code. The current heuristic ignores their preference.
- The Wadler-Lindig doc printer already has the fitting algorithm — the hardcoded threshold short-circuits it.

**Risks:**

- **Existing formatted code will change.** Code formatted with `≥3` items will now potentially collapse. This is a formatting style change, not a bug.
- **"Magic trailing comma" interaction:** Consider adopting Prettier's magic trailing comma behavior: if the user puts a trailing comma on the last item, always explode to multi-line. If they don't, let printWidth decide. This gives users escape-hatch control. We already have `trailingComma = all` which adds commas everywhere; the magic comma would be the inverse — a per-site signal to force expansion.

---

### 3.3 Options REJECTED (with rationale)

| Rejected Option                                                       | Why                                                                                                                                                                       |
| --------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **`braceStyle`** (K&R vs Allman)                                      | Core language identity. Adding this splits the ecosystem. Go, Deno, and Black all pick one style and enforce it. We should too.                                           |
| **`semicolons`** (optional)                                           | Stash's parser requires semicolons. This would be a language change, not a formatter option.                                                                              |
| **`singleQuote`**                                                     | Stash only supports double-quoted strings (`"`). There is no single-quote string syntax. N/A.                                                                             |
| **`arrowParens`** (avoid/always)                                      | Minor aesthetic preference for `x => expr` vs `(x) => expr`. Low value, and the parens form is unambiguous with type hints: `(x: int) => expr`. Not worth the complexity. |
| **`operatorSpacing`**                                                 | Nobody wants `a+b`. Universal standard.                                                                                                                                   |
| **`commentWrapping`**                                                 | Too dangerous. Reformatting comment text can break meaning, links, and embedded code.                                                                                     |
| **Per-construct width thresholds** (fn_call_width, array_width, etc.) | rustfmt-level granularity that creates more confusion than value. A single `printWidth` should drive all decisions.                                                       |
| **`quoteProps`**                                                      | Stash dict keys are always unquoted identifiers or expressions in `[]`. Not applicable.                                                                                   |
| **`objectWrap`** (preserve/collapse)                                  | Prettier added this in v3.5 to address multi-line object heuristics. We should solve this properly via printWidth-driven breaking instead of adding a separate knob.      |

---

## 4. Proposed Option Summary

### After implementation: 9 total options

| #   | Option                    | Type | Default | New?                |
| --- | ------------------------- | ---- | ------- | ------------------- |
| 1   | `indentSize`              | int  | `2`     | Existing            |
| 2   | `useTabs`                 | bool | `false` | Existing            |
| 3   | `printWidth`              | int  | `80`    | Existing (improved) |
| 4   | `trailingComma`           | enum | `none`  | Existing            |
| 5   | `endOfLine`               | enum | `lf`    | Existing            |
| 6   | `bracketSpacing`          | bool | `true`  | Existing            |
| 7   | `sortImports`             | bool | `false` | **NEW**             |
| 8   | `blankLinesBetweenBlocks` | int  | `1`     | **NEW**             |
| 9   | `singleLineBlocks`        | bool | `false` | **NEW**             |

### Implementation Priority

| Priority | Option                             | Effort | Impact                                                         |
| -------- | ---------------------------------- | ------ | -------------------------------------------------------------- |
| **P0**   | Fix printWidth collection breaking | Medium | High — makes the existing option actually work as users expect |
| **P1**   | `sortImports`                      | Medium | High — universally useful, off by default is safe              |
| **P2**   | `blankLinesBetweenBlocks`          | Low    | Medium — simple integer change to blank line emission          |
| **P3**   | `singleLineBlocks`                 | Medium | Medium — needs careful printWidth interaction                  |

---

## 5. Config File Impact

### .stashformat (full example with all 9 options)

```ini
# .stashformat — Stash formatter configuration
indentSize = 2
useTabs = false
printWidth = 80
trailingComma = none
endOfLine = lf
bracketSpacing = true
sortImports = false
blankLinesBetweenBlocks = 1
singleLineBlocks = false
```

### .editorconfig (with custom properties)

```ini
[*.stash]
indent_style = space
indent_size = 2
max_line_length = 80
end_of_line = lf
stash_trailing_comma = none
stash_bracket_spacing = true
stash_sort_imports = false
stash_blank_lines_between_blocks = 1
stash_single_line_blocks = false
```

---

## 6. Design Philosophy Statement

> **Stash's formatter is opinionated by default, configurable where it matters.**
>
> We follow Prettier's principle: options should only exist where reasonable people frequently disagree. If 90%+ of developers would choose the same setting, it should be hardcoded.
>
> Our 9 options cover the 5 choices that genuinely vary between teams and projects: indentation style, line width, trailing commas, import ordering, and code density. Everything else — brace placement, operator spacing, semicolons, quote style — is decided for you.

---

## 7. Implementation Checklist (for Orchestrator)

For each new option:

- [ ] Add property to `FormatConfig` with `{ get; init; }` and default value
- [ ] Add parsing to `FormatConfig.ParseContent()` (.stashformat)
- [ ] Add parsing to `EditorConfigParser` (.editorconfig)
- [ ] Add CLI override to `FormatOptions` (nullable property)
- [ ] Add CLI flag parsing to `FormatOptions.Parse()`
- [ ] Add CLI override merge in `FormatRunner.BuildConfig()` and `Program.HandleStdin()`
- [ ] Add usage text line in `Program.Usage`
- [ ] Implement formatting behavior in `StashFormatter.cs` (and/or `DocPrinter.cs`)
- [ ] Add tests (unit tests for config parsing + formatting behavior tests)
- [ ] Update `Stash.Format/README.md`
- [ ] Update `.stashformat` example file
- [ ] Update `docs/Stash — Standard Library Reference.md` if relevant
