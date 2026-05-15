# @stash/diff

Line-oriented diff for files and strings — structured hunks plus unified-diff rendering, written entirely in [Stash](https://github.com/TheKrystalShip/stash-lang).

This is a pure-Stash package: no language extensions, no new built-ins. It demonstrates that the standard library is expressive enough to ship non-trivial utilities as libraries instead of language primitives.

## Installation

```bash
stash pkg install @stash/diff
```

## Quick Start

```stash
import "@stash/diff" as diff;

let result = diff.lines("foo\nbar\nqux\n", "foo\nbaz\nqux\n", null);
io.println(diff.render.renderUnified(result));
```

Output:

```
--- a
+++ b
@@ -1,3 +1,3 @@
 foo
-bar
+baz
 qux
```

## API Overview

### Top-level functions (`diff.*`)

| Function                    | Returns      | Description                                  |
| --------------------------- | ------------ | -------------------------------------------- |
| `lines(a, b, opts)`         | `DiffResult` | Compare two strings line-by-line.            |
| `files(pathA, pathB, opts)` | `DiffResult` | Read two files via `fs.*` and diff.          |
| `equal(a, b, opts)`         | `bool`       | True iff inputs are line-equal under `opts`. |

`opts` may be `null` for defaults.

### Rendering (`diff.render.*`)

| Function                 | Returns  | Description                                                             |
| ------------------------ | -------- | ----------------------------------------------------------------------- |
| `renderUnified(result)`  | `string` | Standard `diff -u`-compatible output.                                   |
| `colorize(text, enable)` | `string` | Wrap a rendered diff with ANSI color codes. No-op if `enable` is false. |

Side-by-side rendering is deferred to v1.1.

### Types (`diff.types.*`)

```stash
enum Op            { EQUAL, INSERT, DELETE }
enum DiffAlgorithm { MYERS, PATIENCE, HISTOGRAM }
enum WhitespaceMode { NONE, IGNORE_ALL, IGNORE_TRAILING }

struct Edit         { op, oldLine, newLine, text }
struct Hunk         { oldStart, oldCount, newStart, newCount, edits }
struct DiffResult   { hunks, insertions, deletions, equal, aLabel, bLabel,
                      aMissingNewline, bMissingNewline }
struct DiffOptions  { algorithm, contextLines, whitespace, ignoreCase,
                      ignoreBlankLines, preserveLineEndings, maxLines,
                      aLabel, bLabel }
```

### Defaults

| Option                | Default               | Notes                                                                          |
| --------------------- | --------------------- | ------------------------------------------------------------------------------ |
| `algorithm`           | `DiffAlgorithm.MYERS` | `PATIENCE` is opt-in. `HISTOGRAM` falls back to `PATIENCE` in v1.0.            |
| `contextLines`        | `3`                   | Lines of unchanged context around each hunk.                                   |
| `whitespace`          | `WhitespaceMode.NONE` | `IGNORE_ALL` collapses whitespace runs; `IGNORE_TRAILING` trims trailing only. |
| `ignoreCase`          | `false`               |                                                                                |
| `ignoreBlankLines`    | `false`               | Blank-after-trim lines are skipped from comparison.                            |
| `preserveLineEndings` | `false`               | When false, CRLF normalizes to LF on input.                                    |
| `maxLines`            | `0` (unlimited)       | Throw `ValueError` if `len(a)+len(b)` exceeds this.                            |
| `aLabel` / `bLabel`   | `"a"` / `"b"`         | `files` substitutes the file paths when defaults are in effect.                |

## Examples

### Config drift check

```stash
import "@stash/diff" as diff;

let result = diff.diff.files(
  "/var/lib/myapp/nginx.conf.last-good",
  "/etc/nginx/nginx.conf",
  null
);

if (result.equal) {
  io.println("No drift; safe to reload.");
} else {
  io.println($"Drift detected: {result.insertions} inserted, {result.deletions} deleted.");
  io.println(diff.render.renderUnified(result));
}
```

### Log triage — what is new in this rotation?

```stash
import "@stash/diff" as diff;

let yesterday = fs.readFile("/var/log/app.log.1");
let today     = fs.readFile("/var/log/app.log");

let result = diff.lines(yesterday, today, null);
for (let h in result.hunks) {
  for (let e in h.edits) {
    if (e.op == diff.types.Op.INSERT) {
      io.println(e.text);
    }
  }
}
```

### Patience diff with ignored whitespace

```stash
import "@stash/diff" as diff;

let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.PATIENCE,
  contextLines: 5,
  whitespace: diff.types.WhitespaceMode.IGNORE_ALL,
  ignoreCase: false,
  ignoreBlankLines: false,
  preserveLineEndings: false,
  maxLines: 0,
  aLabel: "rendered.yaml (previous)",
  bLabel: "rendered.yaml (new)"
};

let result = diff.diff.files("/tmp/prev.yaml", "/tmp/new.yaml", opts);
io.println(diff.render.colorize(diff.render.renderUnified(result), true));
```

## Out of Scope

The following are explicitly **not** part of this package; each can be a follow-up package:

- **Binary diffing** (suggest: `@stash/bindiff`).
- **Three-way merge** (`@stash/merge3`).
- **Patch application** (`@stash/patch`).
- **Dict / array structural diff** (`@stash/diff-struct`).
- **Word-level / intra-line diff** — v1.1 candidate.
- **Git-format diffs with file modes, rename detection** — belongs in `@stash/git`.

## Performance Notes

Myers is O((N+M)D) where D is the edit distance, with a single O(N+M) band allocation. Patience trims common prefixes/suffixes first and recurses on the bands between unique-line anchors, falling back to Myers on anchorless sub-ranges.

For very large inputs (multi-MB logs, full-disk configs) where in-process diff is too slow, shell out to `diff(1)` via `process.exec` and parse its output instead. Use the `maxLines` option to bail out early.

## License

GPL-3.0
