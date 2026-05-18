# Authoring Stash Packages

Instructions for creating or editing a Stash package in this tree. Auto-loaded when an agent works under `examples/packages/`.

The authoritative CLI/manifest spec is `docs/PKG — Package Manager CLI.md`. This file does NOT re-document the CLI; it tells you how to author an idiomatic package without reading that spec end-to-end. Consult the spec only for CLI flag details, registry/auth flow, or lock-file internals.

## When to use this guide

Use it when you are producing a reusable, importable unit shipped via `stash pkg` — i.e. a directory with `stash.json` + `index.stash` that another project will `import`.

Do NOT use it for one-off scripts, demo `.stash` files in `examples/`, or internal tooling. Those go as a single `.stash` file, no manifest.

## Directory layout (canonical)

```text
<package-name>/
├── stash.json          # manifest — required
├── index.stash         # entry point — required (re-exports lib modules)
├── lib/                # implementation modules — required by convention
│   ├── types.stash
│   ├── <feature>.stash
│   └── ...
├── README.md           # required for publish; user-facing docs
├── LICENSE             # required for publish; SPDX file
├── stashes/            # installed deps — only when the package itself has deps; gitignored
└── stash-lock.json     # only when the package has deps; commit it
```

Rules:

- Every implementation file goes in `lib/`. Do not place feature modules at the package root next to `index.stash`.
- `index.stash` is a thin re-export surface — see "Entry point" below. Do not put logic in it.
- Do not commit `stashes/`. Do commit `stash-lock.json` when present.
- No `tests/` directory convention exists in published packages — see "Tests".

## Manifest (`stash.json`)

Required to publish: `name`, `version`. Everything else is optional but the conventions below are followed by every package in this repo.

Minimal:

```json
{
  "name": "@myorg/widget",
  "version": "0.1.0"
}
```

Idiomatic (matches `@stash/log`, `@stash/cli`, `@stash/docker`):

```json
{
  "name": "@stash/widget",
  "version": "1.0.0",
  "description": "One-line description — what it does, who it's for",
  "author": "TheKrystalShip",
  "license": "GPL-3.0",
  "main": "index.stash",
  "repository": "https://github.com/TheKrystalShip/stash-lang",
  "keywords": ["domain", "tags", "for", "search"],
  "stash": ">=1.0.0",
  "files": ["lib/", "index.stash", "README.md", "LICENSE"],
  "private": false,
  "dependencies": {
    "@stash/cli": "^1.0.0"
  }
}
```

| Field | Required | Notes |
| --- | --- | --- |
| `name` | publish | Unscoped `^[a-z][a-z0-9-]*$` or scoped `^@[a-z][a-z0-9-]*/[a-z][a-z0-9-]*$`. Max 64 chars. Case-sensitive. |
| `version` | publish | SemVer. `0.x` is allowed but caret rules narrow (see "Versioning"). |
| `description` | no | One line. Shown in registry search. |
| `main` | no | Defaults to `index.stash`. Keep the default unless you have a strong reason. |
| `stash` | no | Minimum interpreter version. Use `">=1.0.0"` unless you depend on a newer feature. |
| `files` | no | Publish allowlist. Set this — the default packs more than you want. The canonical value is `["lib/", "index.stash", "README.md", "LICENSE"]`. |
| `dependencies` | no | See "Dependencies". |
| `private` | no | `true` blocks `stash pkg publish`. Use for internal-only packages. |
| `keywords` | no | Lowercase tags for registry search. |

Pitfalls:

- `name` is case-sensitive. `@Stash/Log` is a different package from `@stash/log`.
- `main` is metadata — it does not change how `import "<pkg>"` resolves at runtime. The resolver still goes to the package root and the conventional entry is `index.stash`. Keep them aligned.
- Omitting `files` will ship dotfiles and stray drafts. Always set it.

## Entry point and module structure

`index.stash` exists to give consumers two import shapes from one package:

1. Whole-package namespace: `import "@stash/log" as log; log.logger.info(...)`
2. Submodule selective: `import { info } from "@stash/log/lib/logger.stash";`

The entry point is just a fan-out of namespace imports of `lib/` modules. Every public surface lives in `lib/`. Example, copied from `@stash/cli/index.stash`:

```stash
/// @stash/cli — Shared CLI wrapper toolkit for Stash.
///
/// Import the whole package:
///   import "@stash/cli" as cli;
///   cli.exec.exec("docker", "ps -a");
///
/// Or import individual modules:
///   import { exec } from "@stash/cli/lib/exec.stash";

import "lib/exec.stash" as exec;
import "lib/flags.stash" as flags;
import "lib/parse.stash" as parse;
import "lib/tools.stash" as tools;
```

Rules and conventions for `lib/`:

- One module per feature area (`types.stash`, `logger.stash`, `format.stash`).
- Module-private names start with `_underscore`. They are still importable (Stash defaults to "everything visible") — the underscore is a documented convention, not enforcement. If you want hard privacy, use the `export` keyword to opt the module into restricted visibility (see Language Spec §"Exports").
- Inter-module imports inside `lib/` use bare relative paths: `import "types.stash" as types;` — NOT `./types.stash`, NOT absolute, NOT package-name self-imports.
- Module-scoped `let` at the top of a file is the idiomatic singleton — imports are cached, so the state persists across consumers.
- Doc comments use `///` and precede every exported symbol. The `index.stash` header doc should show both import shapes.

## Dependencies

Declare in `stash.json` under `dependencies`:

```json
"dependencies": {
  "@stash/cli": "^1.0.0",
  "internal-tools": "git:https://github.com/corp/tools.git#v2.0.0"
}
```

| Source | Form | Resolves from |
| --- | --- | --- |
| Registry package | `"<name>": "<range>"` | Configured registry (`STASH_REGISTRY_URL` or `~/.stash/config.json`) |
| Git | `"<name>": "git:<url>#<ref>"` | Git clone at `<ref>` — tag, branch, or SHA |

Version ranges follow SemVer with caret-narrowing for `0.x`:

| Range | Means |
| --- | --- |
| `^1.2.3` | `>=1.2.3 <2.0.0` |
| `^0.2.3` | `>=0.2.3 <0.3.0` |
| `~1.2.3` | `>=1.2.3 <1.3.0` |
| `1.2.3` | exact |
| `*` | any (matches pre-releases too) |

Resolution is flat — every package name resolves to exactly one version across the tree. Diamond conflicts fail loudly; do not assume nested versions are allowed.

After editing `dependencies`, run `stash pkg install` to refresh `stash-lock.json`. Commit the lock file with the manifest change in the same commit.

## Versioning

SemVer, strictly:

- Bump **patch** for bug fixes that preserve the import surface and behavior.
- Bump **minor** for additive changes (new exported functions, new optional fields).
- Bump **major** for any breaking change: removed/renamed export, changed function signature, changed return shape, changed default behavior.

`0.x` is treated as "every minor bump is breaking" by the resolver (see caret rules above). Stay on `0.x` while iterating; cut `1.0.0` when the public surface is stable.

Published versions are immutable. Never reuse a version number — bump and republish.

## Tests

There is no enforced test layout for packages, and the example packages in this tree ship without an in-package test directory. When you do add tests:

- Place them outside the publish set so they do not ship: keep `files` set to `["lib/", "index.stash", "README.md", "LICENSE"]` and put tests under `tests/` at the package root, or under a top-level `tests/` next to the package directory.
- Name test files `<feature>_test.stash` or `test_<feature>.stash` and run them with `stash <file>`.
- For project-wide test infrastructure (xUnit-driven), see `AGENTS.md` — that is the Stash interpreter test suite, not package tests.

If you add an in-package `tests/` directory, also add a `.stashignore` containing `tests/` to keep it out of the published tarball even if someone forgets the `files` whitelist.

## README

The package README is user-facing — not this file. It must contain:

1. Package name as H1, one-line tagline.
2. `stash pkg install <name>` snippet.
3. Quick-start: the shortest import + call that does something visible.
4. Usage section covering both import shapes (whole-package and submodule).
5. API reference or link to one.
6. License line.

Match the tone of `examples/packages/log/README.md`.

## Common pitfalls

- **Forgetting `files`** — ships drafts, editor swap files, and `stashes/`. Always set the whitelist.
- **Putting logic in `index.stash`** — breaks the "consumers can choose import shape" contract. Re-exports only.
- **Self-importing the package name from inside the package** — use bare relative paths inside `lib/`. `import "lib/x.stash"` is fine in `index.stash`; inside `lib/y.stash` use `import "x.stash"`.
- **Underscore prefix as security** — it is convention only. Use the `export` keyword if you need real privacy.
- **Missing LICENSE file** — `license` in the manifest is just an identifier; ship the actual `LICENSE` file so the publish tarball is complete.
- **Editing `stash-lock.json` by hand** — never. Re-run `stash pkg install` or `stash pkg update`.
- **Registering deps but not running install** — the lock file goes stale. Always install after editing `dependencies`.
- **Scoped names with uppercase** — scoped names must be all lowercase; the regex rejects `@stash/Log`.

## Verification checklist

Before declaring a package ready:

```bash
# 1. Manifest + tarball contents are sane
stash pkg pack
# Inspect the printed file list — only files under `files` should appear.

# 2. Dependency graph resolves and lock is current
stash pkg install
stash pkg list

# 3. Entry point loads cleanly (no syntax/import errors)
stash -c 'import "<package-name>" as pkg;'
# Run from a consumer project that has the package installed,
# or from the package directory itself for a local smoke test:
stash index.stash

# 4. If the package has tests
stash tests/*.stash   # adjust to your layout

# 5. Dry-run publish (does not require auth if --registry omitted and pack succeeds)
stash pkg pack
# Inspect, then:
stash pkg publish   # only when you actually want to publish
```

Do not run `stash pkg publish` from agent sessions unless the user explicitly asks. Publishing is irreversible — versions are immutable.

## Minimal copy-pasteable skeleton

`stash.json`:

```json
{
  "name": "@myorg/greet",
  "version": "0.1.0",
  "description": "Tiny greeting helpers for Stash",
  "author": "Your Name",
  "license": "MIT",
  "main": "index.stash",
  "stash": ">=1.0.0",
  "files": ["lib/", "index.stash", "README.md", "LICENSE"],
  "private": false
}
```

`index.stash`:

```stash
/// @myorg/greet — tiny greeting helpers.
///
/// Whole-package import:
///   import "@myorg/greet" as greet;
///   greet.hello.say("world");
///
/// Submodule import:
///   import { say } from "@myorg/greet/lib/hello.stash";

import "lib/hello.stash" as hello;
```

`lib/hello.stash`:

```stash
const DEFAULT_TARGET = "world";

/// Print a greeting to stdout.
fn say(name) {
  let target = name == null ? DEFAULT_TARGET : name;
  io.println("hello, " + target);
}
```

`README.md`: one-line tagline, install snippet, quick-start, usage, license. Keep it short.

`LICENSE`: actual SPDX text matching the `license` field.

That is a complete, publishable package.
