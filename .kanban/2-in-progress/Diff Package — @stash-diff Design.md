# Diff Package — `@stash/diff` Design

> **Status:** Draft
> **Created:** 2026-05-15
> **Type:** Pure-Stash package (no language or stdlib changes)
> **Depends on:** Stdlib only (`fs.*`, `str.*`, `arr.*`, `io.*`, `conv.*`)
> **Related:** [Unique Language Concepts — Volume 1, §6 "Structured diff as a language primitive"](../language/Unique%20Language%20Concepts%20—%20Volume%201.md), [System Utility Packages — Prioritized Roadmap](System%20Utility%20Packages%20—%20Prioritized%20Roadmap.md)

---

## 1. Purpose & Motivation

`@stash/diff` provides line-oriented diffing for files and strings — the building block for config drift detection, deployment verification, log comparison, and snapshot-based auditing. It is delivered as a Stash package installable via `stash pkg install @stash/diff`, written entirely on top of the existing language and standard library.

### 1.1 Why a package, not a language primitive

Volume 1 of the "Unique Language Concepts" backlog proposes making `diff()` a global function in the language, dispatching over strings, arrays, and dicts. That proposal should be **rejected** for the following reasons:

| Concern                  | Built-in `diff()` global                                                                         | `@stash/diff` package                                                         |
| ------------------------ | ------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------- |
| Surface area in core     | New global, polymorphic over 3+ container shapes, new structured result types baked into runtime | Zero — pure Stash, uses stdlib only                                           |
| Iteration speed          | Each change requires interpreter rebuild, language version bump, full regression run             | Patch a `.stash` file, bump package version, publish                          |
| Algorithm choices        | Frozen — picking Myers vs. patience for the core ties everyone to that tradeoff                  | Caller picks via `DiffAlgorithm` enum; package can ship multiple              |
| Output schema            | Must be stable forever once shipped in the language                                              | Can evolve with semver of the package                                         |
| Demonstrates Stash       | Hides the language behind a magic primitive                                                      | **Proves** Stash is expressive enough to ship a non-trivial library in itself |
| Cost of getting it wrong | Permanent legacy in the language reference                                                       | Deprecate the package version, ship v2                                        |

The "diff is a fundamental sysadmin operation" claim is true; the conclusion "therefore it must be a language primitive" does not follow. Bash, Python, and Ruby all leave diff to a library (`diff(1)`, `difflib`, `diffy`) and that has not held them back. The right test for a language primitive is "would a high-quality library version be impossible or substantially uglier?" — and for line diff, the answer is clearly no.

### 1.2 Sysadmin use cases

- **Config drift detection** — compare `/etc/nginx/nginx.conf` against a known-good snapshot before reload.
- **Deployment verification** — diff the rendered template against the previous deploy's manifest; abort if forbidden keys changed.
- **Audit and forensics** — diff `/etc/passwd`, `/etc/sudoers`, or shell history files across two timestamps captured by a backup job.
- **Log triage** — diff two rotated logs to surface only new patterns of errors.
- **PR-style review in scripts** — produce unified-diff output for inclusion in alert emails, Slack notifications, or change-control tickets.

---

## 2. Package Metadata

### 2.1 `stash.json`

```json
{
  "name": "@stash/diff",
  "version": "1.0.0",
  "description": "Line-oriented diff for files and strings — structured hunks plus unified-diff rendering, in pure Stash",
  "author": "TheKrystalShip",
  "license": "GPL-3.0",
  "main": "index.stash",
  "repository": "https://github.com/TheKrystalShip/stash-lang",
  "keywords": ["diff", "text", "compare", "sysadmin", "config", "audit"],
  "stash": ">=1.0.0",
  "files": ["lib/", "index.stash", "README.md", "LICENSE"],
  "private": false
}
```

### 2.2 Directory layout

```
@stash/diff/
  stash.json
  index.stash                # Re-exports public modules
  lib/
    types.stash              # Enums, structs (Op, Hunk, DiffResult, DiffOptions)
    constants.stash          # Named constants (markers, separators, defaults)
    lcs.stash                # Internal LCS / shortest-edit-script engine
    diff.stash               # diffLines / diffFiles entry points
    render.stash             # Unified renderer, side-by-side renderer, ANSI colorizer
  README.md
  LICENSE
```

This mirrors the structure of `@stash/log`: an `index.stash` that imports each `lib/*.stash` module under a sub-namespace, plus a `types.stash` that holds enums and structs.

---

## 3. Public API

All examples assume `import "@stash/diff" as diff;`.

### 3.1 Types (`lib/types.stash`)

```stash
/// Operation tag for each line in a diff result.
/// Bare strings ("add", "delete") are forbidden — always use this enum.
enum Op {
    EQUAL,
    INSERT,
    DELETE
}

/// Which algorithm to run. Caller-selected so we can ship more later
/// without breaking signatures.
enum DiffAlgorithm {
    MYERS,        // O((N+M)D) edit script — default
    PATIENCE,     // Anchored on unique lines — nicer hunks for source code
    HISTOGRAM     // Patience variant — better for files with many duplicates
}

/// A single edit in the diff stream.
struct Edit {
    op,           // Op
    oldLine,      // int | null — 1-based line number in `a`, null for INSERT
    newLine,      // int | null — 1-based line number in `b`, null for DELETE
    text          // string — line content WITHOUT trailing newline
}

/// A contiguous run of changes plus its surrounding context.
struct Hunk {
    oldStart,     // int — 1-based start line in `a`
    oldCount,     // int
    newStart,     // int — 1-based start line in `b`
    newCount,     // int
    edits         // [Edit]
}

/// Top-level result returned by diffLines / diffFiles.
struct DiffResult {
    hunks,        // [Hunk]
    insertions,   // int — total INSERT count
    deletions,    // int — total DELETE count
    equal,        // bool — true iff hunks is empty
    aLabel,       // string — label used in headers (e.g. file path or "a")
    bLabel        // string
}

/// Caller-supplied options.
struct DiffOptions {
    algorithm,    // DiffAlgorithm — default MYERS
    contextLines, // int — default 3 (lines of unchanged context around hunks)
    ignoreWhitespace,    // bool — default false; collapses runs of spaces/tabs before compare
    ignoreCase,          // bool — default false
    ignoreBlankLines,    // bool — default false; lines that are empty after trim are skipped
    aLabel,       // string — default "a"
    bLabel        // string — default "b"
}
```

### 3.2 Constants (`lib/constants.stash`)

Per the project rule against magic literals, every recurring string is named here:

```stash
const MARKER_INSERT    = "+";
const MARKER_DEconstE    = "-";
const MARKER_EQUAL     = " ";
const MARKER_NO_NEWLINE = "\\ No newline at end of file";
const HUNK_HEADER_OPEN  = "@@ ";
const HUNK_HEADER_CLOSE = " @@";
const FILE_HEADER_A    = "--- ";
const FILE_HEADER_B    = "+++ ";
const DEFAULT_CONTEXT_LINES = 3;
const LINE_SEPARATOR_LF = "\n";
const LINE_SEPARATOR_CRLF = "\r\n";

/// ANSI escape codes for color rendering. Disabled by default — caller must opt in.
const ANSI_RESET  = "\x1b[0m";
const ANSI_RED    = "\x1b[31m";
const ANSI_GREEN  = "\x1b[32m";
const ANSI_CYAN   = "\x1b[36m";
const ANSI_BOLD   = "\x1b[1m";
```

### 3.3 Entry points (`lib/diff.stash`)

```stash
/// Compare two strings as line streams. Returns a DiffResult.
fn diffLines(a: string, b: string, opts: DiffOptions = null) -> DiffResult

/// Read both files via fs.* and diff them. Labels default to the supplied paths.
fn diffFiles(pathA: string, pathB: string, opts: DiffOptions = null) -> DiffResult

/// True iff `a` and `b` are line-equal under the supplied options.
fn equal(a: string, b: string, opts: DiffOptions = null) -> bool
```

### 3.4 Rendering (`lib/render.stash`)

```stash
/// Produce a standard unified-diff string, suitable for `patch(1)` consumers,
/// Slack/email payloads, or commit messages.
fn renderUnified(result: DiffResult) -> string

/// Two-column rendering for terminal display. Wraps long lines if width is set.
fn renderSideBySide(result: DiffResult, width: int = 80) -> string

/// Wraps any renderer's output with ANSI colors. No-op if `enable` is false,
/// so callers can pass `term.supportsColor()` directly.
fn colorize(text: string, enable: bool) -> string
```

---

## 4. Algorithm Choice

**Recommendation:** Default to **Myers' O((N+M)D) shortest-edit-script** algorithm, with `PATIENCE` as an opt-in second algorithm in v1.0.

### 4.1 Tradeoffs considered

| Algorithm                | Correctness                              | Complexity to implement in Stash                                  | Hunk quality on configs/logs                                             | Performance on large files                                    |
| ------------------------ | ---------------------------------------- | ----------------------------------------------------------------- | ------------------------------------------------------------------------ | ------------------------------------------------------------- |
| **Naive LCS (DP table)** | Optimal SES                              | Simplest                                                          | Identical to Myers                                                       | O(N\*M) memory — dies at ~10k lines                           |
| **Myers**                | Optimal SES                              | ~150 lines of Stash                                               | Good; occasionally produces oddly-aligned hunks on duplicate-heavy files | O((N+M)D) — fine up to multi-MB files where the diff is small |
| **Patience**             | Not optimal SES, but more human-readable | ~200 lines (needs unique-line anchoring + recursive LCS on bands) | Best on source-like inputs with many duplicate blank/brace lines         | Comparable to Myers in practice                               |
| **Histogram**            | Variant of patience                      | ~250 lines                                                        | Best on files with many repeated short lines                             | Comparable to Myers                                           |

### 4.2 Decision

- **v1.0** ships **Myers** (default) + **Patience** (opt-in via `DiffOptions.algorithm`).
- Naive LCS is rejected as the default because the O(N\*M) memory hit makes it unusable for the very files sysadmins care about (logs, large config bundles).
- Histogram is deferred to v1.1; patience covers the same use cases for v1.

The algorithm engine lives in `lib/lcs.stash` behind a private `_computeEditScript(a_lines, b_lines, algorithm) -> [Edit]` function. Both `diff.stash` entry points call into it, then walk the edit list to build hunks with the configured context window. This isolation lets us add algorithms later without touching the public surface.

### 4.3 Risk: pure-Stash performance

Diff is allocation-heavy and array-index-heavy. Myers in particular touches O(D) values where D is the edit distance.

- **Mitigation:** the algorithm only allocates the `v[]` band that Myers requires — no full N\*M table. Stash's bytecode VM and inline-caching work (already shipped, see `Inline Caching — Shape-Based Field Access.md` in `4-done/`) should bring per-iteration overhead within order-of-magnitude of Python's `difflib`. For files large enough to stress this, callers should shell out to `diff(1)` via `process.exec` — and the package README will say so.
- **Bail-out:** add a `maxLines` option (default unlimited) so callers can fail fast on inputs known to be too large for an in-process diff.

---

## 5. Data Structures — Worked Example

Diffing:

```
a:                b:
foo               foo
bar               baz
qux               qux
```

Produces:

```stash
DiffResult {
    hunks: [
        Hunk {
            oldStart: 1, oldCount: 3,
            newStart: 1, newCount: 3,
            edits: [
                Edit { op: Op.EQUAL,  oldLine: 1, newLine: 1, text: "foo" },
                Edit { op: Op.DELETE, oldLine: 2, newLine: null, text: "bar" },
                Edit { op: Op.INSERT, oldLine: null, newLine: 2, text: "baz" },
                Edit { op: Op.EQUAL,  oldLine: 3, newLine: 3, text: "qux" }
            ]
        }
    ],
    insertions: 1,
    deletions: 1,
    equal: false,
    aLabel: "a",
    bLabel: "b"
}
```

`renderUnified` on this returns:

```
--- a
+++ b
@@ -1,3 +1,3 @@
 foo
-bar
+baz
 qux
```

---

## 6. Examples

### 6.1 Config drift check before reload

```stash
import "@stash/diff" as diff;

let snapshot = "/var/lib/myapp/nginx.conf.last-good";
let current  = "/etc/nginx/nginx.conf";

let result = diff.diffFiles(snapshot, current);

if (result.equal) {
    io.println("No drift; safe to reload.");
} else {
    io.println("Drift detected: ${result.insertions} inserted, ${result.deletions} deleted.");
    io.println(diff.renderUnified(result));
    process.exit(1);
}
```

### 6.2 Deployment verification with colored side-by-side output

```stash
import "@stash/diff" as diff;
import "@stash/diff/lib/types.stash" as dt;

let opts = dt.DiffOptions {
    algorithm: dt.DiffAlgorithm.PATIENCE,
    contextLines: 5,
    ignoreWhitespace: true,
    aLabel: "rendered.yaml (previous)",
    bLabel: "rendered.yaml (new)"
};

let result = diff.diffFiles("/tmp/prev.yaml", "/tmp/new.yaml", opts);
let body = diff.renderSideBySide(result, 120);
io.println(diff.colorize(body, term.supportsColor()));
```

### 6.3 Log triage — what's new in this rotation

```stash
import "@stash/diff" as diff;

let yesterday = fs.readFile("/var/log/app.log.1");
let today     = fs.readFile("/var/log/app.log");

let result = diff.diffLines(yesterday, today);
// Only print INSERTed lines — these are new log entries.
for (let h in result.hunks) {
    for (let e in h.edits) {
        if (e.op == diff.types.Op.INSERT) {
            io.println(e.text);
        }
    }
}
```

---

## 7. Testing Strategy

Tests ship as `.stash` scripts under the package's `tests/` directory and are exercised via the existing `Stash.Tests` package-loading harness (same pattern used by `@stash/log`'s migration tests).

Required scenarios:

1. **Identity** — `diffLines(x, x)` returns `equal: true`, zero hunks, regardless of algorithm.
2. **Fully disjoint** — every line of `a` becomes DELETE, every line of `b` becomes INSERT.
3. **Single-line insert / delete / change** in the middle of a file — verify hunk line numbers.
4. **Multiple non-adjacent hunks** — verify `contextLines` correctly merges or splits hunks.
5. **Edge cases:**
   - Empty `a`, non-empty `b` (all INSERT).
   - Non-empty `a`, empty `b` (all DELETE).
   - Empty strings on both sides.
   - File without trailing newline — renderer emits `MARKER_NO_NEWLINE`.
   - CRLF vs LF line endings — splitter normalizes.
6. **Option flags:**
   - `ignoreWhitespace` — lines differing only in spaces compare equal.
   - `ignoreCase` — `Foo` vs `foo` compare equal.
   - `ignoreBlankLines` — interleaved blank lines do not produce edits.
7. **Algorithm parity** — for several fixture pairs, both `MYERS` and `PATIENCE` produce results with the same insertion/deletion **counts** even if hunk shapes differ. (Hunk-shape equality is not required.)
8. **Rendering round-trip** — `renderUnified(result)` output is byte-identical to a `diff -u` fixture captured from GNU diff, for a curated set of small inputs.
9. **Large-file smoke test** — 5,000 lines on each side with a 50-line change, completes under 2s on the CI baseline.
10. **Binary safety** — feeding bytes that contain a NUL throws a clear `InvalidInputError` rather than silently corrupting output. (This package is explicitly text-only; see §8.)

---

## 8. Out of Scope (v1.0)

These are useful but explicitly **not** part of this package. Each can be a follow-up package:

- **Binary diffing** — byte-level or block-based diff (suggest: `@stash/bindiff`).
- **Three-way merge** — needs a base revision and conflict markers (`@stash/merge3`).
- **Patch application** — applying a unified diff to a file. Distinct algorithm (fuzz matching, hunk offset adjustment); deserves its own package (`@stash/patch`).
- **Dict / array structural diff** — Volume 1's pitch covered dicts (`{ port: 8080 → 9090 }`) and arrays (`missing`, `extra`). These are semantically different problems and pollute the line-diff API. Defer to a separate `@stash/diff-struct` package.
- **Word-level / intra-line diff** — highlighting changed characters within a line. Useful but doubles the surface area; v1.1 candidate.
- **Git-format diffs with file modes, rename detection, similarity index** — out of scope for a general utility; would belong in a `@stash/git` package.

---

## 9. Open Questions

These need user input before implementation begins:

1. **Newline handling on input.** Should `diffLines` normalize CRLF → LF before comparing, always preserve the original style in output, or be configurable? Recommended default: normalize on input, emit LF in `renderUnified` (matches `diff -u`), but offer a `DiffOptions.preserveLineEndings` flag.
2. **`ignoreWhitespace` semantics.** Two common meanings:
   - "Treat any run of whitespace as a single space" (Git's `-w`).
   - "Trim leading/trailing whitespace only" (Git's `-b`).
     Recommended: expose both as `WhitespaceMode.IGNORE_ALL` / `WhitespaceMode.IGNORE_TRAILING` enum, replacing the bool. Confirm.
3. **Return type when `equal == true`.** Always return a `DiffResult` with empty hunks (predictable), or return `null` (allows `if (result == null)` idiom)? Recommended: always return a struct; null-returns are an antipattern.
4. **Public visibility of `Edit`.** Most callers will only touch `Hunk` and `DiffResult`. Should `Edit` be in the public types module or an internal one? Recommended: public — callers that build custom renderers need it.
5. **Package name.** `@stash/diff` or `@stash/text-diff`? The latter leaves room for `@stash/diff-struct`, `@stash/bindiff` later without name collision; the former is shorter and matches Volume 1's mental model. Recommend `@stash/diff` and qualify the others (`@stash/diff-struct`, `@stash/bindiff`).
6. **Renderer split.** Is `renderSideBySide` worth shipping in v1.0, or should v1.0 be unified-only and side-by-side land in v1.1? Recommended: defer side-by-side; it's terminal-width sensitive and inflates the test matrix.
7. **Dependency on `term.supportsColor()`.** Does the stdlib expose a TTY/color-capability check today? If not, `colorize` takes a plain `bool` and the README documents the typical idiom (`io.stderr.isTty()` or similar). Needs verification against current stdlib.

Answer: Use the recommended answers for all questions.

---

## 10. Decision Log

| Date       | Decision                                                                              | Alternatives                                    | Rationale                                                                                                                             |
| ---------- | ------------------------------------------------------------------------------------- | ----------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-05-15 | Ship as `@stash/diff` package, **not** as a language global.                          | Build-in `diff()` per Volume 1 §6.              | Avoids permanent core surface, enables independent iteration, demonstrates Stash's expressiveness as a library platform.              |
| 2026-05-15 | Default algorithm = Myers; opt-in Patience.                                           | Naive LCS, Histogram-only, patience-as-default. | Myers gives optimal edit scripts with O((N+M)D) cost; patience covers the "nice hunks for code" case without owning the default.      |
| 2026-05-15 | Line-oriented only in v1.0; dict/array/word diffs are separate packages.              | One mega-package per Volume 1.                  | Each has a distinct algorithm and result shape; bundling them muddies the API.                                                        |
| 2026-05-15 | All operation tags exposed as `Op` enum; no bare `"add"`/`"delete"` strings anywhere. | String tags per Volume 1's example.             | Project rule against magic strings; enum gives autocomplete, type-checkability, and refactor-safety.                                  |
| 2026-05-15 | Renderer is a separate module from the algorithm.                                     | One-shot `diffString(a, b) -> string`.          | Lets callers inspect `DiffResult` programmatically (filter inserts only, count drift, build alerts) without re-parsing rendered text. |

---

## 11. Implementation Notes for the Orchestrator

When this spec moves to `1-todo/`, the implementer should:

1. Scaffold the package directory under `examples/packages/diff/` matching the layout in §2.2.
2. Implement `lib/lcs.stash` first; cover with unit tests against fixtures before any other module touches it.
3. Implement `lib/diff.stash` (hunk assembly with `contextLines`) on top of the LCS module.
4. Implement `lib/render.stash` last.
5. Resolve every Open Question (§9) with the user before locking the v1.0 API.
6. Add an entry to `docs/Stash — Standard Library Reference.md`? **No** — packages are not stdlib. The package's own `README.md` is the user-facing reference.
7. No language-spec, LSP, DAP, formatter, playground, tmGrammar, or analysis changes are required. This is the central point of this spec.
