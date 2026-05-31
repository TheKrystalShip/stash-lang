# `path.match(path, pattern)` — pure path-vs-glob predicate

Status: proposed
Created: 2026-05-31
Discovery context: Investigating the "rewrite `scripts/checkpoint/*` in Stash" dogfooding feature. A readiness spike against the installed `stash 0.5.0` binary confirmed Stash can express every checkpoint script *except* the scope check in `verify-phase.sh`, which needs a pure path-vs-glob predicate that does not exist today.

## Motivation

`scripts/checkpoint/verify-phase.sh` enforces phase file-scope by matching each **changed-file path string** (from `git diff --name-only`) against the phase's `files` / `scope` glob patterns (`**`, `*`, extglob) **without touching the filesystem** — the changed file may have just been deleted, and the patterns are gitignore-style, not FS-expansion queries.

Stash has no primitive for this:

- `fs.glob(pattern)` expands a pattern against the **real filesystem** (`Directory.GetFiles(..., AllDirectories)`); it cannot test an arbitrary (possibly non-existent) path string against a pattern.
- `path.*` is pure string manipulation (`dir`/`base`/`ext`/`join`/`normalize`/`relative`/…) — no matcher.
- `re.test` is regex, not glob; callers would have to hand-roll glob→regex conversion (and risk diverging from bash `globstar`/`extglob` semantics).

This is the **single blocking stdlib gap** for the checkpoint-script rewrite.

## Proposed API

`path.match(path: string, pattern: string) -> bool` — ungated (pure, side-effect free, no capability). Returns whether `path` matches the glob `pattern`. Must support `**` (cross-segment), `*` (within-segment), `?`, and character classes, matching the semantics `verify-phase.sh` relies on (bash `globstar`).

Open question for the spec: reuse the compiler-level glob expander already in the codebase (the explore pass noted glob logic exists at the compiler level) vs. a fresh matcher in `PathBuiltIns`. Prefer reuse if the semantics line up.

## Readiness spike ledger (verified against `stash 0.5.0`, 2026-05-31)

Everything else the rewrite needs already works — capabilities are granted to bare `stash script.stash` invocations with no flags:

| Capability / primitive | Result |
| --- | --- |
| YAML round-trip deterministic + key-order-preserving | PASS — `stringify∘parse` fixpoint is stable; top-level key order preserved |
| `yaml.stringify` needs `sort_keys`? | No — insertion order already preserved by default (== Python `sort_keys=False`) |
| `yaml.stringify` needs `width` folding? | Optional parity only — Stash emits long scalars unfolded (acceptable); one-time reformat on cutover |
| FileSystem capability (read/write/move/delete) | Granted by default |
| Process capability — `git` via `process.exec` | Granted by default; `exitCode` + `stdout`/`stderr` captured |
| `fs.move` atomic rename (the `save_yaml` tmp→dst pattern) | Works |
| `env.get` / `env.exit(code)` exit-code signaling | `env.exit(3)` → process exit code 3 |
| `path.match(path, pattern)` | **MISSING — this stub** |

YAML emitter style differs cosmetically from PyYAML (double quotes vs single; unfolded long scalars). Not a blocker: once Stash owns the files, repeated writes are byte-stable. Cutover produces one reformatting commit per file.

## Language-changes.md checklist (this is a stdlib addition)

- `PathBuiltIns` implementation + registration in the `path` namespace builder.
- `[StashFn]` / `[StashParam]` + XML `<summary>`/`<param>`/`<returns>`/`<exception>` metadata; regenerate `docs/Stash — Standard Library Reference.md` via `dotnet run --project Stash.Docs/`.
- LSP/completion: new function surfaces in `CompletionHandler`; re-baseline `CompletionSurfaceSnapshotTests`.
- `Wave1ThrowsCoverageTests`: `path.match` is an infallible pure read → add to `NoThrowAllowList` (or give it throws metadata if it can fault on a bad pattern).
- Example `.stash` demonstrating glob matching.
- xUnit tests: `**`/`*`/`?`/classes, non-existent paths, edge cases, parity with the bash semantics `verify-phase.sh` needs.

## Guardrails carried forward (fold into the spec[s])

1. **Acceptance for `path.match` is bash-decision parity, not "matches globs."** `verify-phase.sh:64-72` matches under bash `globstar extglob` (`[[ == ]]`). .NET globbers (e.g. `Microsoft.Extensions.FileSystemGlobbing`) disagree with bash on edge cases — whether `**` matches zero segments, whether `*` crosses `/`. Validate against the **actual patterns in real `plan.yaml` files**, confirming the *same in/out-of-scope decision* bash makes today — not just synthetic `**`/`*` cases. A "correct" primitive that silently changes which files count as in-scope is a regression.

2. **Install gate is a hard ordering boundary between readiness and rewrite — make it an explicit `done_when`.** `~/.local/bin/stash` is 0.5.0 and lacks `path.match`; any rewritten Stash script that calls it cannot run until the binary is rebuilt (`build.stash`) and reinstalled. Bake into the readiness feature's final `done_when`: *new `stash` installed to `~/.local/bin`, and `stash -c 'io.println(path.match("a/b.cs","a/**"))'` prints `true`.* The rewrite feature must not be specced until this gate is green.

3. **Differential testing of the rewrite must be SEMANTIC, not byte-diff.** Because the YAML emitter differs cosmetically from PyYAML (proven: `out1 == original` is `false`), comparing python-script output to stash-script output as text fails on every file even when identical in meaning. Assert instead: same **exit code** AND `yaml.parse(python_out) == yaml.parse(stash_out)` (parsed-structure equality). Build the rewrite spec's verify commands on parsed comparison from day one.

## Related

- Parent feature: rewrite `scripts/checkpoint/*` (bash+python) in Stash as language dogfooding. Decisions locked: pinned installed `stash` binary for invocation (dissolves the bootstrapping paradox); add this `path.match` primitive rather than glob→regex in-script; verify Stash readiness *before* rewriting (this stub is that verification).
- `verify-phase.sh:64-72` — the scope-matching loop this primitive replaces.
