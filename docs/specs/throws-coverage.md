# Throws Coverage Tracking

Living document tracking `[StashFn(Throws=...)]` metadata coverage across stdlib namespaces. Wave 1 namespaces (high-traffic, source-generator-attributed) are prioritised for machine-readable throws metadata. Remaining namespaces document errors in prose `**Throws:**` callouts in the Standard Library Reference.

## Wave 1 — High-Traffic (Source Generator + `[StashFn(Throws=...)]`)

These namespaces have been migrated to the source-generator form and carry structured throws metadata consumed by LSP tooling.

| Namespace  | Coverage   | Notes                                                               |
| ---------- | ---------- | ------------------------------------------------------------------- |
| `fs`       | ✅ Tagged  | All public functions annotated; `IOError`, `ValueError`, `TypeError` |
| `process`  | ✅ Tagged  | All public functions annotated; `CommandError`, `ValueError`, `TypeError` |
| `io`       | ✅ Tagged  | All public functions annotated; `IOError`                           |
| `conv`     | ✅ Tagged  | All public functions annotated; `ParseError`, `TypeError`, `ValueError` |
| `json`     | ✅ Tagged  | All public functions annotated; `ParseError`, `TypeError`           |
| `http`     | ✅ Tagged  | All public functions annotated; `IOError`, `TimeoutError`           |

## Wave 2+ — Pending Attribution

Remaining namespaces use hand-written `BuiltInFunction` delegates. Throws information is documented in the Standard Library Reference prose callouts only (not machine-readable via `[StashFn(Throws=...)]`).

| Namespace    | Notes                                                              |
| ------------ | ------------------------------------------------------------------ |
| `str`        | `TypeError`, `ValueError`, `ParseError` documented in prose       |
| `arr`        | `TypeError`, `IndexError` documented in prose                     |
| `dict`       | `TypeError`, `KeyError` documented in prose                       |
| `math`       | `TypeError`, `ValueError` documented in prose                     |
| `time`       | `TypeError`, `ValueError`, `ParseError` documented in prose       |
| `path`       | `TypeError`, `ValueError` documented in prose                     |
| `env`        | `TypeError`, `ValueError` documented in prose                     |
| `re`         | `TypeError`, `ValueError`, `ParseError` documented in prose       |
| `buf`        | `TypeError`, `IndexError` documented in prose                     |
| `xml`        | `ParseError`, `TypeError` documented in prose                     |
| `ini`        | `ParseError`, `TypeError` documented in prose                     |
| `yaml`       | `ParseError`, `TypeError` documented in prose                     |
| `csv`        | `ParseError`, `TypeError` documented in prose                     |
| `archive`    | `IOError`, `TypeError` documented in prose                        |
| `crypto`     | `TypeError`, `ValueError` documented in prose                     |
| `encoding`   | `TypeError`, `ValueError` documented in prose                     |
| `net`        | `IOError`, `TimeoutError` documented in prose                     |
| `sys`        | `TypeError`, `NotSupportedError` documented in prose              |
| `task`       | `TimeoutError`, `CancellationError` documented in prose           |
| `tpl`        | `ParseError`, `IOError` documented in prose                       |
| `alias`      | `AliasError` documented in prose                                  |
| `config`     | `IOError`, `ParseError` documented in prose                       |
| `args`       | `ValueError` documented in prose                                  |
| `lock`       | `LockError` documented in prose                                   |
| `term`       | `IOError`, `NotSupportedError` documented in prose                |

## User-Code Surface

User functions can annotate their own throws via `@throws` doc-comment tags (Phase 2, implemented). The LSP renders these alongside stdlib throws metadata. See [Documenting Throws](../Stash%20—%20Language%20Specification.md#documenting-throws) in the Language Specification.

## How to Migrate a Namespace

1. Ensure the namespace is in source-generator form (attributed with `[StashNamespace]` / `[StashFn]`).
2. Add `[StashFn(Throws = new[] { "ErrorType1", "ErrorType2" })]` to each function.
3. Run `dotnet test --filter "{Namespace}DocSnapshotTests"` to verify metadata roundtrips.
4. Update this table to ✅.
