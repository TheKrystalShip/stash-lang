# Throws Coverage Tracking

Living document tracking `[StashFn(Throws=...)]` metadata coverage across stdlib namespaces. Wave 1 namespaces (high-traffic, source-generator-attributed) are prioritised for machine-readable throws metadata. Remaining namespaces document errors in prose `**Throws:**` callouts in the Standard Library Reference.

## Wave 1 — High-Traffic Foundations (Complete)

| Namespace   | Functions | Tagged | Untagged (intentional) | Coverage                    |
| ----------- | --------- | ------ | ---------------------- | --------------------------- |
| `fs`        | 37        | 26     | 11                     | 100% (allow-listed)         |
| `io`        | 7         | 2      | 5                      | 100% (allow-listed)         |
| `conv`      | 13        | 11     | 2                      | 100% (allow-listed)         |
| `json`      | 4         | 4      | 0                      | 100%                        |
| `http`      | 8         | 8      | 0                      | 100%                        |
| `process`   | 30        | 22     | 8                      | 100% (allow-listed)         |
| **Total**   | **99**    | **73** | **26**                 | **100%**                    |

The "untagged (intentional)" column counts pure query/predicate functions that genuinely throw nothing. Enforced by `Stash.Tests/Stdlib/SourceGenerator/Wave1ThrowsCoverageTests.cs`.

## Wave 2 — Common Namespaces (Pending)

`str`, `arr`, `dict`, `math`, `time`, `path`, `re`, `crypto`, `encoding`, `net`, `env`. ~150 throw sites. Not yet machine-readable.

| Namespace    | Functions | Tagged | Coverage | Notes                              |
| ------------ | --------- | ------ | -------- | ---------------------------------- |
| `str`        | —         | 0      | 0%       | `TypeError`, `ValueError`, `ParseError` documented in prose |
| `arr`        | —         | 0      | 0%       | `TypeError`, `IndexError` documented in prose |
| `dict`       | —         | 0      | 0%       | `TypeError`, `KeyError` documented in prose |
| `math`       | —         | 0      | 0%       | `TypeError`, `ValueError` documented in prose |
| `time`       | —         | 0      | 0%       | `TypeError`, `ValueError`, `ParseError` documented in prose |
| `path`       | —         | 0      | 0%       | `TypeError`, `ValueError` documented in prose |
| `re`         | —         | 0      | 0%       | `TypeError`, `ValueError`, `ParseError` documented in prose |
| `crypto`     | —         | 0      | 0%       | `TypeError`, `ValueError` documented in prose |
| `encoding`   | —         | 0      | 0%       | `TypeError`, `ValueError` documented in prose |
| `net`        | —         | 0      | 0%       | `IOError`, `TimeoutError` documented in prose |
| `env`        | —         | 0      | 0%       | `TypeError`, `ValueError` documented in prose |

## Wave 3 — Specialized Namespaces (Pending)

`buf`, `xml`, `ini`, `yaml`, `csv`, `archive`, `tpl`, `task`. ~100 throw sites. Not yet machine-readable.

| Namespace    | Functions | Tagged | Coverage | Notes                              |
| ------------ | --------- | ------ | -------- | ---------------------------------- |
| `buf`        | —         | 0      | 0%       | `TypeError`, `IndexError` documented in prose |
| `xml`        | —         | 0      | 0%       | `ParseError`, `TypeError` documented in prose |
| `ini`        | —         | 0      | 0%       | `ParseError`, `TypeError` documented in prose |
| `yaml`       | —         | 0      | 0%       | `ParseError`, `TypeError` documented in prose |
| `csv`        | —         | 0      | 0%       | `ParseError`, `TypeError` documented in prose |
| `archive`    | —         | 0      | 0%       | `IOError`, `TypeError` documented in prose |
| `tpl`        | —         | 0      | 0%       | `ParseError`, `IOError` documented in prose |
| `task`       | —         | 0      | 0%       | `TimeoutError`, `CancellationError` documented in prose |

## Wave 4 — Long Tail (Pending)

`sys`, `term`, `alias`, `config`, `args`, `lock`, `test`, `assert`, and remaining utilities. ~100 throw sites.

| Namespace    | Functions | Tagged | Coverage | Notes                              |
| ------------ | --------- | ------ | -------- | ---------------------------------- |
| `sys`        | —         | 0      | 0%       | `TypeError`, `NotSupportedError` documented in prose |
| `term`       | —         | 0      | 0%       | `IOError`, `NotSupportedError` documented in prose |
| `alias`      | —         | 0      | 0%       | `AliasError` documented in prose |
| `config`     | —         | 0      | 0%       | `IOError`, `ParseError` documented in prose |
| `args`       | —         | 0      | 0%       | `ValueError` documented in prose |
| `lock`       | —         | 0      | 0%       | `LockError` documented in prose |

## User-Code Surface

User functions can annotate their own throws via `@throws` doc-comment tags (Phase 2, implemented). The LSP renders these alongside stdlib throws metadata. See [Documenting Throws](../Stash%20—%20Language%20Specification.md#documenting-throws) in the Language Specification.

## How to Migrate a Namespace

1. Ensure the namespace is in source-generator form (attributed with `[StashNamespace]` / `[StashFn]`).
2. Add `[StashFn(Throws = new[] { "ErrorType1", "ErrorType2" })]` to each function.
3. Run `dotnet test --filter "{Namespace}DocSnapshotTests"` to verify metadata roundtrips.
4. Update this table to ✅.
