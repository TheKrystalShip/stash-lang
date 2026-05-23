# RFC: CLI Argument Parsing — Declarative Stdlib Schema

> **Status:** Draft
> **Owner:** Cristian Moraru
> **Created:** 2026-05-18
> **Slug:** cli-arg-parsing

## Summary

Replace the legacy `args` namespace with a typed, composable `cli` namespace that defines a script's command-line surface as ordinary Stash data. A schema is a value built from stdlib helpers (`cli.schema`, `cli.positional`, `cli.option`, `cli.flag`, `cli.command`) and consumed by `cli.parse` / `cli.tryParse` / `cli.help` / `cli.build`. Raw argv access (`args.list` / `args.count`) folds into `cli` as `cli.argv()` / `cli.argc()`.

```stash
schema = cli.schema({
    input:   cli.positional("path", required: true),
    output:  cli.option("string", short: "o", default: "./out"),
    verbose: cli.flag(short: "v"),
    retries: cli.option("int", default: 3),
})

args = cli.parse(schema)
io.println(args.input)
```

The entire `args` namespace is removed in the same release. Stash is pre-1.0; there is no deprecation phase and no parallel surface. Migration of in-tree callers (`examples/`, `Stash.Tests/`, `docs/`, `examples/packages/.../@stash/cli/`) is handled by a separate follow-up spec.

## Motivation

Stash ships `args.list`, `args.count`, `args.parse`, and `args.build` today (`Stash.Stdlib/BuiltIns/ArgsBuiltIns.cs`). `args.parse` accepts an opaque dict spec and returns an opaque dict, with no notion of help text, subcommands, repeated values, choices, validators, typed conversion beyond `int`/`float`/`bool`, or structured error reporting. Scripts that need any of those features walk `args.list()` by hand or maintain the `@stash/cli` user-space package — a Stash-level reimplementation of the same idea, written precisely because the stdlib lacked it.

A dedicated `args { ... }` *syntax* would be a second mini-language: concise for the happy path, awkward for subcommands, reusable groups, computed defaults, shared validators, or tested parser behavior. Representing the CLI surface as **data built from stdlib helpers** keeps composition, testing, and tool discovery natural and avoids parser changes.

## Goals

- Land a typed `cli` namespace with the full API surface listed in [Design / Surface](#surface) (12 functions plus the five model structs).
- Validate schemas at construction time via `CliSchemaError`; surface parse failures as nine typed `[StashError]` subclasses.
- Provide both `cli.parse` (exits on failure; default for scripts) and `cli.tryParse` (never exits; for tests and libraries).
- Generate `--help` text from any schema without script execution.
- Round-trip parsed values back to argv via `cli.build`.
- LSP support for literal schemas: hover, completion on the parsed-result identifier, duplicate-short diagnostics, unknown type-tag diagnostics.
- `stash --help script.stash` literal-only static discovery — emit help for fully-literal top-level `schema = cli.schema({...})` bindings without running the script.
- Remove the `args` namespace cleanly (in this release; migration of callers is a follow-up).
- Update `docs/Stash — Standard Library Reference.md` via the regenerator and add example scripts.

## Non-Goals

- **No new syntax.** This is a stdlib + analyzer feature. If implementation pulls syntax into the parser, that is a design regression.
- **No `args` deprecation phase.** No `[StashDeprecated]` shim, no compatibility layer. The namespace is removed.
- **No migration of in-tree callers** (`Stash.Tests/Interpreting/ArgsNamespaceTests.cs`, `DictArgParseTests.cs`, `ArgsBuildTests.cs`, `CliExecutionTests.cs`, `examples/service_ctl.stash`, the `@stash/cli` package set). Tracked by a separate follow-up spec.
- **No sandboxed sub-interpreter for `stash --help`.** v1 ships the literal-only static path; non-literal schemas fall back to a generic message. Richer evaluation is deferred.
- **No shell-completion API (`cli.completion` or other).** Removed entirely from the v1 surface; a follow-up spec owns shell completion design.
- **No composition helpers (`cli.extend`, schema merge).** Manual dict-level merging covers v1.
- **No `"path"` / `"uri"` type tags.** Neither is a first-class Stash type today; use `"string"` in v1 and reintroduce when the types exist.
- **No `choices: SomeEnum` sugar.** v1 accepts only arrays of primitives; revisit once user-defined enums settle.

## Design

### Surface

New namespace: `cli`.

| Function                              | Returns          | Description                                                          |
| ------------------------------------- | ---------------- | -------------------------------------------------------------------- |
| `cli.argv()`                          | `array<string>`  | Raw script argv as supplied by the host. Replaces `args.list`.       |
| `cli.argc()`                          | `int`            | Number of raw script arguments. Replaces `args.count`.               |
| `cli.schema(definition)`              | `CliSchema`      | Validate a dict definition and build a reusable schema value.        |
| `cli.positional(type, options?)`      | `CliArgSpec`     | Declare a positional argument.                                       |
| `cli.option(type, options?)`          | `CliArgSpec`     | Declare a named option that takes a value.                           |
| `cli.flag(options?)`                  | `CliArgSpec`     | Declare a boolean flag.                                              |
| `cli.command(definition)`             | `CliCommandSpec` | Declare a set of named subcommands.                                  |
| `cli.parse(schema, argv?)`            | `dict`           | Parse argv. On failure: print help, exit 2.                          |
| `cli.tryParse(schema, argv?)`         | `CliParseResult` | Parse argv. Never exits.                                             |
| `cli.help(schema, options?)`          | `string`         | Render the schema as `--help` text.                                  |
| `cli.printHelp(schema, options?)`     | `null`           | Convenience: `io.println(cli.help(schema, ...))`.                    |
| `cli.build(schema, values)`           | `array<string>`  | Render a values dict back to an argv string array.                   |

`cli.argv()` / `cli.argc()` are zero-arg `[StashFn]` functions, not property-style namespace members. Stash namespaces have no precedent for property-style access (verified against `EnvBuiltIns`, `ArgsBuiltIns`). They are **ungated** — no `RequiresCapability(...)` annotation — matching the existing `args.list` / `args.count` behavior and the `EnvBuiltIns` zero-arg accessor precedent. Reading host-decoded argv is not a sandbox-escape vector.

#### Model types

All exposed as `[StashStruct]` in `CliBuiltIns`.

- **`CliSchema`** — opaque to user code; carries positional specs, option specs by name, optional `CliCommandSpec`, and metadata (program name, description). Read-only.
- **`CliArgSpec`** — a positional, option, or flag specification.
- **`CliCommandSpec`** — maps subcommand names to nested `CliSchema` values.
- **`CliCommand`** — `{ name: string, path: array<string>, values: dict }`. The result-side type for the selected subcommand.
- **`CliParseResult`** — `{ ok: bool, value: dict, error: Error?, helpRequested: bool }`. Return type of `cli.tryParse`.

#### Type tags

Type tags are **strings** because Stash type names are not first-class values (verified: `let x = int;` raises `RuntimeError: Undefined variable 'int'`). Supported tags for v1:

| Tag          | Conversion source                                                                       |
| ------------ | --------------------------------------------------------------------------------------- |
| `"string"`   | identity                                                                                |
| `"int"`      | `conv.toInt`                                                                            |
| `"float"`    | `conv.toFloat`                                                                          |
| `"bool"`     | `conv.toBool` (`"true"`/`"false"`/`"1"`/`"0"`/`"yes"`/`"no"`, case-insensitive)         |
| `"duration"` | `time.duration(...)` parsing (e.g. `"30s"`, `"5m"`)                                     |
| `"ip"`       | `net.ip(...)` parsing                                                                   |
| `"bytesize"` | bytesize literal parsing                                                                |
| `"semver"`   | semver parsing                                                                          |

Unrecognised tags are `CliSchemaError` at `cli.schema(...)` time, not at parse time.

#### Spec option keys

- `cli.positional(type, options?)`: `name`, `required` (default true), `default`, `repeated`, `choices`, `validate`, `help`, `metavar`.
- `cli.option(type, options?)`: `name`, `short`, `aliases`, `required`, `default`, `repeated`, `choices`, `min`/`max`, `pattern`, `validate`, `help`, `metavar`, `env`.
- `cli.flag(options?)`: `name`, `short`, `aliases`, `help`, `default` (default false), `negatable`.

See the detailed tables in §4.2–§4.4 of the source spec for the full per-key default + semantic notes.

### Semantics

#### Property naming

Dict keys in the schema definition are the Stash-side property names on the parsed result (idiomatic camelCase). The CLI-side spelling defaults to a kebab-cased copy of the key (`dryRun` → `--dry-run`), overridable via `name: "..."`.

#### Flag and option grammar

- `--name value`, `--name=value`, `-n value`, `-nVALUE` all accepted.
- `--no-name` only accepted when the flag declares `negatable: true`.
- `-abc` is short-flag bundling for boolean flags. The last short in a bundle may take a value (`-vfFILE` → `-v -f FILE`).
- `--` ends option parsing; subsequent tokens are positionals.
- Unknown options raise `CliUnknownOption`. No general passthrough mode in v1.

#### Validation order (per argument)

1. Token-level (missing value, unknown option, ambiguous abbreviation).
2. Type conversion via the type tag. Failure → `CliInvalidValue`.
3. `choices` membership. Failure → `CliInvalidValue` with `expected: choices`.
4. `min` / `max` / `pattern`. Failure → `CliValidationFailed`.
5. User `validate` callback `(value) -> bool | string`, invoked as an `IStashCallable` (same mechanism `arr.map`, `arr.filter`, `task.run` use). `false` produces a generic failure; a returned string is used as the message.

After all per-argument validation, missing-required checks run for required positionals and options without defaults.

#### Help flag

Every schema implicitly accepts `--help` / `-h`. Schemas may opt out via `helpFlag: false`. Shadowing `--help` or `-h` while `helpFlag` is enabled is a `CliSchemaError`.

#### Subcommands

Tokens before the first non-option positional are parsed against the root schema (root flags / global options visible). The next non-option token selects the subcommand; unknown name → `CliUnknownCommand`. Remaining tokens parse against the selected schema. Help flags at the subcommand position print subcommand-scoped help and exit 0. Global flags declared at the root carry through to subcommands.

#### Default argv source

`cli.parse(schema)` / `cli.tryParse(schema)` default `argv` to `cli.argv()` — i.e. `IInterpreterContext.ScriptArgs`. Tests, REPL experimentation, and child-process simulation pass an explicit `argv` array.

#### Static `--help` discovery

`stash --help script.stash` runs entirely through the analyzer — **no script execution**. The analyzer looks for a top-level binding named `schema` (overridable via a top-of-file `// @cli-schema-binding: <name>` comment marker, parsed by `Stash.Analysis`) whose initializer is a literal `cli.schema({...})` call.

v1 scope is **literal-only**. A schema value is "literal" iff it is one of: `IntegerLiteral`, `FloatLiteral`, `StringLiteral`, `BoolLiteral`, `NullLiteral`, `IpAddressLiteral`, `DurationLiteral`, `ByteSizeLiteral`, `SemVerLiteral`, an `ArrayLiteral`/`DictLiteral` whose values are all literal (recursive), or a `UnaryExpr(Minus, IntegerLiteral | FloatLiteral)` — the last case admits `default: -1` since the parser does not fold negative-number tokens at lex time ([Stash.Core/Parsing/Parser.cs:2067](../../Stash.Core/Parsing/Parser.cs#L2067)).

Other unary forms (`!true`, `~0`, `--x`, `-someVariable`) are **non-literal** and force the fallback message. Non-literal schemas degrade gracefully — no diagnostic, no execution — printing: `usage: stash <script> [args...]\n\nNo statically discoverable CLI schema; run the script with --help for full usage if it supports it.`

### Error model

Nine new `[StashError]` subclasses of `RuntimeError` in `Stash.Core/Runtime/Errors/`:

| Error type               | When thrown                                                                      | Properties                                |
| ------------------------ | -------------------------------------------------------------------------------- | ----------------------------------------- |
| `CliSchemaError`         | Schema construction fails (duplicate names, invalid default, unknown type tag).  | `field: string`, `reason: string`         |
| `CliMissingRequired`     | A required positional or option was not supplied.                                | `name: string`                            |
| `CliUnknownOption`       | An option not declared in the schema was encountered.                            | `option: string`                          |
| `CliMissingValue`        | An option that requires a value appeared without one.                            | `option: string`                          |
| `CliInvalidValue`        | Type conversion or `choices` membership failed.                                  | `option: string?`, `value: string`, `expected: string` |
| `CliUnexpectedPositional`| Extra positional after all positional slots are filled.                          | `value: string`                           |
| `CliAmbiguousOption`     | A long-option prefix matches more than one declared option.                      | `option: string`, `candidates: array<string>` |
| `CliValidationFailed`    | `min`/`max`/`pattern`/`validate` rejected the value.                             | `option: string?`, `message: string`      |
| `CliUnknownCommand`      | A subcommand name was not declared.                                              | `name: string`, `candidates: array<string>` |

`cli.parse` catches these and translates them to a formatted stderr message + `exit(2)`. `cli.tryParse` exposes them as `result.error`.

### Implementation Path

```
Stash.Stdlib defines the CliBuiltIns namespace, the schema struct types,
    and the builder helpers (cli.schema/positional/option/flag/command)
  -> Stash.Core registers the nine [StashError] subclasses through
       BuiltInErrorRegistry, so try/catch sees structured properties
  -> CliBuiltIns implements the parsing engine (tryParse), then the
       subcommand stage, then the validation pipeline (incl. IStashCallable
       callbacks dispatched the same way arr.map dispatches)
  -> CliBuiltIns implements cli.help / cli.printHelp; cli.parse wraps
       cli.tryParse with stderr formatting and exit(0)/exit(2) calls
  -> CliBuiltIns implements cli.build (inverse); cli.argv / cli.argc are
       wired from IInterpreterContext.ScriptArgs; ArgsBuiltIns is deleted
       and the namespace deregistered
  -> Stash.Analysis recognises a literal cli.schema(...) bound to a
       top-level identifier (default `schema`, overridable via the
       `// @cli-schema-binding: <name>` comment) and surfaces diagnostics
       (duplicate short, unknown type tag) and completion (`args.<field>`)
  -> Stash.Cli adds `--help script.stash` mode: invoke the analyzer to
       extract the literal schema, build the CliSchema directly from the
       parsed literal tree, print cli.help, exit 0; otherwise print the
       generic fallback message
  -> Stash.Docs regenerates `docs/Stash — Standard Library Reference.md`
       from [StashFn]/[StashError] metadata; example scripts demonstrate
       flat and subcommand usage
```

The key invariant the path preserves: **a schema is just data**. Building, inspecting, and rendering it never requires `cli.parse` to run, which is what enables both testing and static `--help` discovery.

### Tooling integration

- **LSP (phase-1):** hover on the parsed-result identifier lists declared fields and type tags; diagnostics for duplicate short options and unknown type tags inside a literal `cli.schema({...})`; completion for `args.<field>` after `cli.parse`. Dynamically constructed schemas degrade gracefully.
- **Generated docs:** `cli` namespace and all nine error types regenerated into `docs/Stash — Standard Library Reference.md` via `dotnet run --project Stash.Docs/`. The error registry already supports the metadata shape.
- **`--disassemble`, `--no-optimize`:** unaffected — `cli.parse` is never reached during disassembly; `cli.schema` literals compile to ordinary dict/call expressions.
- **REPL and `stash -c '...'`:** `cli.argv()` returns `[]`; `cli.parse(schema)` behaves as if invoked with no arguments (help / defaults / `CliMissingRequired`).
- **Sandboxed embeddings:** `cli.parse` calls the existing `exit` global (capability: `Environment`). Embeddings that disable `Environment` and still want CLI parsing should use `cli.tryParse`.

## Acceptance Criteria

**End-to-end behavior:**

1. A script with `schema = cli.schema({...})` and `args = cli.parse(schema, ["--verbose", "foo"])` returns a dict whose keys are the schema dict keys, converted to the declared type tags.
2. `cli.tryParse` round-trip: success populates `value`; each of the nine `CliError` subclasses populates `error` with the documented structured properties; `--help` populates `helpRequested: true` without exiting.
3. Subcommand parsing: a two-level schema (`remote add`) parses `["remote", "add", "--url", "x"]` and produces `args.command` with `path: ["remote", "add"]` and the leaf-schema values populated.
4. `cli.build(schema, cli.parse(schema, argv)) ≈ argv` for a round-trippable schema (no defaults applied / no env overrides).
5. `cli.help(schema)` returns formatted help text covering usage line, positional table, option table, subcommand list, and default annotations.

**Failure-path behavior:**

6. `cli.parse(schema, badArgv)` on a parse error prints a short message + abbreviated usage to stderr and calls `exit(2)`.
7. `cli.parse(schema, ["--help"])` prints `cli.help(schema)` to stdout and calls `exit(0)`.
8. `cli.schema({ a: cli.option("notatype") })` raises `CliSchemaError` at construction.
9. A schema with duplicate short options raises `CliSchemaError` at construction.
10. The `validate` callback returning a string surfaces that string as the `CliValidationFailed.message`.

**Cross-entrypoint behavior:**

11. After this feature ships, `args.list`, `args.count`, `args.parse`, `args.build` all raise the standard "Undefined namespace" / "Unknown function" runtime error.
12. `cli.argv()` / `cli.argc()` return the same data `args.list` / `args.count` did, for each of the three host invocation modes (`stash script.stash a b` → `["a","b"]`; `stash -c '...'` → `[]`; REPL → `[]`).
13. `stash --help script.stash` against a fully-literal schema fixture prints the rendered help and exits 0 without executing the script.
14. `stash --help script.stash` against a non-literal initializer prints the generic fallback message and exits 0.
15. LSP literal-schema fixture: duplicate-short diagnostic and `args.<field>` completion both fire.
16. `dotnet run --project Stash.Docs/` exits clean after regeneration; `docs/Stash — Standard Library Reference.md` includes `cli.*` and all nine error types; `StandardLibraryReferenceTests` pass.

**Static `--help` literal-ness rule:**

17. `default: -1` and `default: -1.5` in a schema are classified as literal.
18. `default: !true`, `default: -x`, and `default: --1` are classified as non-literal (fallback branch).

## Phases

The phase list lives in `plan.yaml`. There are **11 phases**, preserving the structure of §10 of the source spec one-to-one:

| ID  | Title |
| --- | --- |
| P1  | Core schema model + builders                                          |
| P2  | Error registry (nine `[StashError]` subclasses)                       |
| P3  | Parsing engine + `cli.tryParse` (positionals, options, flags, repeated, choices, defaults, `--`, short bundling) |
| P4  | Subcommand parsing (multi-level, global flag passthrough)             |
| P5  | Validation pipeline (`min`/`max`/`pattern`/`validate` callback)       |
| P6  | Help rendering (`cli.help` / `cli.printHelp`)                         |
| P7  | `cli.parse` exit/print wrapper + argv default                         |
| P8  | `cli.build` (inverse) + remove `args` namespace + add `cli.argv`/`cli.argc` |
| P9  | LSP diagnostics + hover/completion                                    |
| P10 | `stash --help script.stash` literal-only static mode                  |
| P11 | Docs regeneration + examples                                          |

The 11-phase shape from §10 of the source spec survived intact. Phases P1–P3 are the load-bearing core; P5 and onward each unlock one user-visible capability. P8 deletes `ArgsBuiltIns.cs` and adds `cli.argv` / `cli.argc` in the same phase because both touch namespace registration.

## Open Questions

All design questions are resolved. Items deferred to follow-up specs are explicit Non-Goals here:

1. **Sandboxed sub-interpreter for `stash --help`.** v1 ships P10 as a literal-only stub. A follow-up spec will define a restricted evaluator that can execute `cli.*` calls and pure expressions over non-literal initializers without running the rest of the script.
2. **Shell completion API.** Removed from the v1 surface entirely. A dedicated spec will own bash/zsh/fish output formats, dynamic completion sources, and the static-discovery path for completion scripts.
3. **`args` → `cli` migration of in-tree code.** This spec removes the `args` namespace at the stdlib level (P8) but does not migrate the in-tree callers in `examples/`, `Stash.Tests/`, and the `examples/packages/.../@stash/cli/` package set. A separate spec sequences those edits and decides whether the `@stash/cli` package files are rewritten or left as legacy snapshots.
4. **Composition helpers (`cli.extend`, schema merge).** Deferred; manual dict-level merging covers v1.
5. **`"path"` / `"uri"` type tags.** Deferred until first-class `path` / `uri` types exist. Use `"string"` in v1.
6. **Choices vs Stash enums.** Once user-defined enums settle, allow `choices: SomeEnum` as syntactic sugar. v1 accepts only arrays of primitives.

## Decision Log

| Date       | Decision                                                                            | Rationale                                                                                                                                                  |
| ---------- | ----------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-05-18 | Use a stdlib schema API, not dedicated argument syntax.                             | Schemas compose, can be tested, reused across modules, and don't require parser changes.                                                                   |
| 2026-05-18 | Type tags are strings (`"int"`, `"duration"`, …), not bare type identifiers.        | Verified: Stash does not have type-as-value semantics today; `let x = int;` raises `RuntimeError`. String tags keep the API working without language extension. |
| 2026-05-18 | Drop `"path"` and `"uri"` from the v1 type-tag set.                                 | Neither is a first-class type in `Stash — Language Specification.md §Values and Types`. Use `"string"` in v1; reintroduce when the types exist.            |
| 2026-05-18 | Remove the entire `args` namespace; raw argv moves to `cli.argv()` / `cli.argc()`.  | Stash is pre-1.0; we are not carrying a parallel API. Zero-arg `[StashFn]` functions match the rest of the stdlib (verified against `EnvBuiltIns`).        |
| 2026-05-18 | Sub-command result is a named `CliCommand` struct, not a loose dict.                | Matches existing precedent (`RegexMatch`, `TcpConnection`); enables LSP field completion and stable docs.                                                  |
| 2026-05-18 | `cli.parse` exits on failure; `cli.tryParse` returns a structured result.           | Production scripts want the convenient default; tests and libraries can't tolerate process exits.                                                          |
| 2026-05-18 | Static `stash --help` uses a documented binding convention, not runtime introspection. | Runtime introspection requires executing the script. A literal-only convention lets the analyzer surface help without side effects.                       |
| 2026-05-18 | Validator callback signature: `(value) -> bool \| string`.                          | Mirrors familiar patterns; supports `IStashCallable` invocation already used by `arr.map`, `arr.filter`, `task.run`.                                       |
| 2026-05-18 | Static-discovery convention is a top-level binding named `schema`, overridable via `// @cli-schema-binding: <name>` comment. | The alternative — requiring an explicit `cli.main(schema, fn)` call — adds a runtime entry-point concept that doesn't otherwise exist in Stash. The comment escape hatch handles scripts that need a different binding name without a parser change. |
| 2026-05-18 | `stash --help script.stash` ships in v1 as a literal-only stub.                     | The sandboxed sub-interpreter is a non-trivial subsystem (capability whitelist, error model, GC isolation); shipping it inside this spec would balloon scope. The literal-only path covers the common case (hand-written schemas). |
| 2026-05-18 | `cli.completion` is removed from the v1 API surface entirely.                       | A placeholder that returns `""` is a worse footgun than no function at all. Defer the entire surface; the follow-up spec is free to pick a different shape. |
| 2026-05-18 | The `@stash/cli` user-space package will be deleted wholesale, not migrated.        | The package only existed because the stdlib lacked typed argument parsing; once `cli.*` ships, its entire reason for being is subsumed. Rewriting it would produce a thin wrapper around the stdlib that adds no value. |
| 2026-05-18 | "Literal" for static `--help` includes `UnaryExpr(Minus, IntegerLiteral\|FloatLiteral)`, no other unary forms. | The parser does not fold negative-number tokens at lex time, so `default: -1` is always `UnaryExpr(Minus, IntegerLiteral(1))`. Restricting admission to a directly-nested numeric literal keeps the predicate trivial and excludes side-effecting forms like `!true`, `~0`, `--x`, `-someVar`. |
| 2026-05-18 | `cli.argv()` / `cli.argc()` are ungated — no `RequiresCapability` annotation.       | Reading the host-decoded argv string array is not a sandbox-escape vector. The host already decides what `ScriptArgs` contains before the script runs. Matches the existing `args.list` / `args.count` ungated behavior and the `EnvBuiltIns` zero-arg accessor precedent. |
