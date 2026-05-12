# Throws Coverage Tracking

Living document tracking `[StashFn(Throws=...)]` metadata coverage across stdlib namespaces. Wave 1 namespaces (high-traffic, source-generator-attributed) are prioritised for machine-readable throws metadata. Remaining namespaces document errors in prose `**Throws:**` callouts in the Standard Library Reference.

## Wave 1 — High-Traffic Foundations (Complete)

| Namespace | Functions | Tagged | Untagged (intentional) | Coverage            |
| --------- | --------- | ------ | ---------------------- | ------------------- |
| `fs`      | 37        | 26     | 11                     | 100% (allow-listed) |
| `io`      | 7         | 2      | 5                      | 100% (allow-listed) |
| `conv`    | 13        | 11     | 2                      | 100% (allow-listed) |
| `json`    | 4         | 4      | 0                      | 100%                |
| `http`    | 8         | 8      | 0                      | 100%                |
| `process` | 30        | 22     | 8                      | 100% (allow-listed) |
| **Total** | **99**    | **73** | **26**                 | **100%**            |

The "untagged (intentional)" column counts pure query/predicate functions that genuinely throw nothing. Enforced by `Stash.Tests/Stdlib/SourceGenerator/Wave1ThrowsCoverageTests.cs`.

## Wave 2 — Common Namespaces (Complete)

| Namespace  | Functions | Tagged  | Untagged (intentional) | Coverage            |
| ---------- | --------- | ------- | ---------------------- | ------------------- |
| `str`      | 43        | 27      | 16                     | 100% (allow-listed) |
| `arr`      | 47        | 28      | 19                     | 100% (allow-listed) |
| `dict`     | 21        | 5       | 16                     | 100% (allow-listed) |
| `math`     | 23        | 10      | 13                     | 100% (allow-listed) |
| `time`     | 31        | 16      | 15                     | 100% (allow-listed) |
| `path`     | 10        | 9       | 1                      | 100% (allow-listed) |
| `re`       | 6         | 6       | 0                      | 100%                |
| `crypto`   | 16        | 7       | 9                      | 100% (allow-listed) |
| `encoding` | 8         | 8       | 0                      | 100%                |
| `net`      | 38        | 37      | 1                      | 100% (allow-listed) |
| `env`      | 21        | 7       | 14                     | 100% (allow-listed) |
| **Total**  | **264**   | **160** | **104**                | **100%**            |

The "untagged (intentional)" column counts pure query/predicate functions that genuinely throw nothing (e.g. `str.upper`, `arr.contains`, `time.now`). Enforced by `Stash.Tests/Stdlib/SourceGenerator/Wave2ThrowsCoverageTests.cs`.

## Wave 3 — Specialized Namespaces (Pending)

`buf`, `xml`, `ini`, `yaml`, `csv`, `archive`, `tpl`, `task`. ~100 throw sites. Not yet machine-readable.

| Namespace | Functions | Tagged | Coverage | Notes                                                   |
| --------- | --------- | ------ | -------- | ------------------------------------------------------- |
| `buf`     | —         | 0      | 0%       | `TypeError`, `IndexError` documented in prose           |
| `xml`     | —         | 0      | 0%       | `ParseError`, `TypeError` documented in prose           |
| `ini`     | —         | 0      | 0%       | `ParseError`, `TypeError` documented in prose           |
| `yaml`    | —         | 0      | 0%       | `ParseError`, `TypeError` documented in prose           |
| `csv`     | —         | 0      | 0%       | `ParseError`, `TypeError` documented in prose           |
| `archive` | —         | 0      | 0%       | `IOError`, `TypeError` documented in prose              |
| `tpl`     | —         | 0      | 0%       | `ParseError`, `IOError` documented in prose             |
| `task`    | —         | 0      | 0%       | `TimeoutError`, `CancellationError` documented in prose |

## Wave 4 — Long Tail (Pending)

`sys`, `term`, `alias`, `config`, `args`, `lock`, `test`, `assert`, and remaining utilities. ~100 throw sites.

| Namespace | Functions | Tagged | Coverage | Notes                                                |
| --------- | --------- | ------ | -------- | ---------------------------------------------------- |
| `sys`     | —         | 0      | 0%       | `TypeError`, `NotSupportedError` documented in prose |
| `term`    | —         | 0      | 0%       | `IOError`, `NotSupportedError` documented in prose   |
| `alias`   | —         | 0      | 0%       | `AliasError` documented in prose                     |
| `config`  | —         | 0      | 0%       | `IOError`, `ParseError` documented in prose          |
| `args`    | —         | 0      | 0%       | `ValueError` documented in prose                     |
| `lock`    | —         | 0      | 0%       | `LockError` documented in prose                      |

## User-Code Surface

User functions can annotate their own throws via `@throws` doc-comment tags (Phase 2, implemented). The LSP renders these alongside stdlib throws metadata. See [Documenting Throws](../Stash%20—%20Language%20Specification.md#documenting-throws) in the Language Specification.

## How to Migrate a Namespace

1. Ensure the namespace is in source-generator form (attributed with `[StashNamespace]` / `[StashFn]`).
2. Add `[StashFn(Throws = new[] { "ErrorType1", "ErrorType2" })]` to each function.
3. Run `dotnet test --filter "{Namespace}DocSnapshotTests"` to verify metadata roundtrips.
4. Update this table to ✅.
