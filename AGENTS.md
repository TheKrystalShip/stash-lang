# Stash Language - Project Guidelines

Stash is a cross-platform scripting language for system administration. The interpreter is .NET-based and compiles to native binaries for Linux, macOS, and Windows. It combines C-style syntax with first-class shell command execution (`$(...)` syntax), built-in data structures (structs, enums, dictionaries), and 35 namespaces of standard library functions. Language features and standard library additions must work across all three platforms.

## Release Status — Pre-1.0, No Backwards Compatibility

Stash is **not released yet**. There are no users, no published scripts in the wild, and no stability guarantees. Every change — syntax, semantics, stdlib signatures, AST shape, bytecode format, namespace layout — is free to break previous behavior.

**Implications when implementing changes:**

- **Do not write compatibility shims, deprecation aliases, or migration paths.** If a name, signature, or syntax form changes, update every call site and delete the old form outright. Nothing depends on it.
- **Do not preserve old field/property names "just in case."** Rename freely; the compiler will surface every consumer.
- **Do not gate new behavior behind feature flags or opt-in toggles** unless the flag itself is the feature being designed.
- **Do not leave `// kept for backwards compat` comments or re-export removed types.** Delete them.
- **When refactoring, the goal is the cleanest end state**, not the smallest diff. Touching 40 call sites to rename a parameter is fine; leaving a wrapper that forwards to the new name is not.
- **Existing tests and examples are the only consumers.** If a test asserts the old behavior, update the test to assert the new behavior — don't preserve old behavior to keep the test green.

This applies until the project explicitly declares a v1.0 stability commitment in this file.

## Decision Doctrine — Make It Right, Not Expedient

**At every decision point, default to the option that is correct for the long run — the canonical, convention-following, root-cause solution — even when a cheaper, smaller, or faster option would also "work." Make it right the first time, even if that means a rewrite.** Stash is pre-1.0 with no backwards-compatibility debt (above), so the expedient shortcut buys nothing the rewrite can't: there is never a compatibility tax to dodge, only the cost of the right construction itself.

This is the umbrella over the project's other doctrines, each an instance of it: "no magic strings → a real `enum`, not a `const string` stopgap" (the 100% type-safe fix, not the 80% centralized-string one); "Construct > Detect > Instruct" (make omission *impossible*, don't merely detect it); and "cleanest end state, not smallest diff" (above).

**How it operates:**

- **When weighing options — in a spec, a design call, an `AskUserQuestion` — the long-run-correct option is the default: ordered first, marked recommended.** Choose it autonomously when deciding for yourself; recommend it when the user decides. "Easier to implement," "smaller diff," "fewer files touched," and "faster to land" are **not** reasons to choose the inferior option — they never outrank correctness for the long run.
- **Prefer the durable type/construct over the cheap string/flag/patch.** A real `enum` over a `const string`; an EF value converter over a stringly-typed column; the root-cause fix over a symptom guard; the established pattern over a one-off. If making it right means rewriting working code, rewrite it.
- **A required rewrite is surfaced and scoped, never silent.** When the right choice implies a large change, call it out (a dedicated phase in the spec, a note to the user) and size it — but do not shrink the change to dodge it. Scoping a rewrite is correct; avoiding it to stay small is not.
- **Deferring the right choice is allowed only when deliberate and documented** — an explicit user decision, or a backlog stub stating the rationale and the correct end-state. A silent expedient default — the shortcut taken because it was easier, with no record — is the one thing this doctrine forbids.
- **The user's explicit, informed choice always wins.** Recommend the right option and surface the tradeoff plainly; then follow their call. This doctrine governs *your* defaults and recommendations, not the user's authority to choose otherwise.

**What this is NOT — it is not a license for scope creep or gold-plating.** It governs *how well you solve the agreed problem*, never *how much you build*. "Make it right" means the right depth, correctness, and durability for the in-scope work — it never means adding unrequested features, speculative generality, or polish beyond the requirement. Choosing the right construction ≠ enlarging the task.

## The Specification is the Law

**`docs/Stash — Language Specification.md` is the normative, human-authored definition of Stash — the law, in the spirit of the C++ ISO standard. The implementation exists to *honor* it; the spec is never generated from, derived from, or subordinate to the code.** A user learns how Stash behaves by reading the spec, not the source — so a behavior the spec does not state does not, for that reader, exist. Undocumented observable behavior is not a hidden feature; it is a **gap**, and a gap is a defect. This doctrine sits beside *Make It Right*, but it inverts the usual flow: here the **document leads and the code follows**.

**How it operates:**

- **Sealed, not merely populated.** The spec must state what the language **does** *and* what it **explicitly does not do** — the full observable behavior of every internal system: errors, edges, lifecycles, ordering, concurrency, isolation, resource cleanup, exit semantics. Negative space — "X is not guaranteed," "Y is dropped at exit," "Z is unspecified" — is as binding as positive space. The bar is *airtight*: a competent reader should be able to predict the language's behavior in any situation the spec addresses, and know which situations it deliberately leaves open. A black box where the user must read the source to learn what happens is a failure of this doctrine.
- **Spec-first.** Language or runtime behavior is decided and written **in the spec first**, as the prose a human will read — *then* the code is made to conform. The spec drives development; the diff conforms to the spec, never the reverse. "Implemented and tested but unspecified" is the exact drift this forbids — it quietly promotes the tests to the de-facto source of truth and leaves the spec a stale shadow. (This is how `async` cooperative-cancellation, `task.status` lifecycle, and unobserved-task reporting shipped working-and-tested but undocumented — the failure that motivated this doctrine.)
- **Conformance is tested, not constructed.** The spec is prose *by design* — not machine-parseable, not generated — so omission here **cannot** be made a compile error, and the *Construct > Detect > Instruct* doctrine deliberately does **not** reach it. That is accepted, not a shortcoming: intent written for humans cannot be a testable construct. The spec↔reality binding is instead held by **conformance unit tests** (`Category=Conformance`, see `Stash.Tests/CLAUDE.md`) that assert each explicit claim — positive *and* negative. A normative claim with no conformance test, or a conformance test with no clause behind it, is itself a gap.
- **Drift either way is a bug.** Undocumented observable behavior → write the clause, then the test. A spec claim the code violates → fix the code, or correct the claim if it was wrong. The two must never silently disagree; when they do, decide which is right *on purpose* and make the other match.
- **Seal first, then bend.** When the goal is to lock the standard down, write the *intended* law first and only afterward bend the implementation to honor it — never reverse-engineer the law from whatever the code happens to do today. The standard is the destination; the code is moved to meet it.

The standing program to find and close the gaps — the unwritten rules we follow but never wrote down, the negative space we never stated — is the **`language-standard` milestone** (`.kanban/milestones/language-standard/MILESTONE.md`). Every language/stdlib change also carries the per-change obligations in `.claude/language-changes.md`.

## Architecture

```
Stash.Core          -> Lexer (two-pointer scanner), Parser (recursive-descent), 54 AST node types
Stash.Stdlib        -> Built-in metadata registry, model records, single source of truth for all namespaces
Stash.Bytecode      -> Bytecode VM (compiler + register-based VM), 101 opcodes, 35 built-in namespaces
Stash.Analysis      -> Static analysis engine, rules, resolvers, visitors for diagnostics and tooling
Stash.Cli           -> REPL + script runner (Native AOT)
Stash.Lsp           -> Language Server Protocol (OmniSharp - NOT AOT, requires reflection)
Stash.Dap           -> Debug Adapter Protocol (OmniSharp - NOT AOT, requires reflection)
Stash.Check         -> Static analysis CLI (Native AOT)
Stash.Format        -> Code formatter CLI (Native AOT)
Stash.Docs          -> Reference generators (stdlib metadata + bytecode opcode metadata -> writes docs/)
Stash.Tap           -> TAP test framework runtime
Stash.Tpl           -> Templating engine
Stash.Scheduler     -> Cross-platform OS service management (systemd, launchd, Task Scheduler)
Stash.Playground    -> Browser-based interactive playground (Blazor WASM, Monaco editor)
Stash.Registry      -> Package registry server (ASP.NET Core, EF Core, JWT auth)
Stash.Tests         -> xUnit test suite (5,800+ tests)
```

**Key constraint:** LSP and DAP use OmniSharp/DryIoc which requires reflection. They must **never** be built with Native AOT - only the CLI uses AOT. See `build.stash` for publish commands and binary size guards.

The VS Code extension lives at `.vscode/extensions/stash-lang/` (TypeScript - LSP/DAP clients, TAP test runner, syntax highlighting).

## Project Layering

```
Layer 0 (Foundation)  -> Stash.Core (no dependencies)
Layer 1 (Libraries)   -> Stash.Stdlib, Stash.Analysis, Stash.Tpl, Stash.Scheduler
Layer 2 (Runtime)     -> Stash.Bytecode (depends on Core + Stdlib + Tpl)
Layer 3 (Tooling)     -> Stash.Cli, Stash.Lsp, Stash.Dap, Stash.Check, Stash.Format, Stash.Docs, Stash.Playground, Stash.Registry
Layer 4 (Tests)       -> Stash.Tests, Stash.Tap
```

Core never depends on anything else. Bytecode depends on Stdlib (not the other way around - stdlib built-ins are injected via `IStdlibProvider`). LSP, DAP, and Registry cannot reference each other.

## Build & Test

```bash
dotnet build                            # Build all projects
dotnet test                             # Run all xUnit tests
dotnet run --project Stash.Cli/ -- file.stash   # Run a script
dotnet run --project Stash.Cli/         # Start REPL
```

Test a specific namespace: `dotnet test --filter "FullyQualifiedName~ArrBuiltInsTests"`

## Code Conventions

- **C# style:** File-scoped namespaces, nullable enabled, 4-space indent, LF line endings
- **Naming:** `PascalCase` public, `_camelCase` private fields - enforced in `.editorconfig`
- **var usage:** Never for built-in types (`string`, `int`), always when type is apparent from RHS
- **AST nodes:** Each has a `SourceSpan` for diagnostics. Expression nodes implement `IExprVisitor<T>`, statement nodes `IStmtVisitor<T>`
- **Built-in namespaces:** One file per namespace in `Stash.Stdlib/BuiltIns/` (e.g., `ArrBuiltIns.cs`). Register functions via `BuiltInFunction` delegates
- **Tests:** `{Feature}_{Scenario}_{Expected}()` naming in `Stash.Tests/`, one test file per namespace (`ArrBuiltInsTests.cs`, `DictBuiltInsTests.cs`, etc.)
- **No magic strings or literals:** Never write a string (or other literal) inline when a named reference already exists - use the existing constant, property, or identifier. If no reference exists, create one before using the value. Duplicated literals scattered across the codebase are an absolute failure to follow this rule.

## Key Patterns

- **Visitor pattern:** Six visitors implement `IExprVisitor<T>` and `IStmtVisitor<T>` - Compiler, SemanticResolver, SemanticValidator, SymbolCollector, SemanticTokenWalker, StashFormatter. When adding a new AST node, update ALL visitors
- **Partial classes:** Large visitors are split by responsibility (e.g., `Compiler.cs` has 9 partials: Expressions, ComplexExprs, Collections, Strings, ControlFlow, Declarations, Exceptions, Helpers). Follow this pattern for new visitor logic
- **VM type protocols:** 12 `IVM*` interfaces in `Stash.Core/Runtime/Protocols/` (IVMArithmetic, IVMComparable, IVMEquatable, IVMTruthiness, IVMStringifiable, IVMFieldAccessible, IVMFieldMutable, IVMIndexable, IVMIterable, IVMIterator, IVMSized, IVMTyped). All domain types implement relevant protocols - never add hardcoded type cascades to VM dispatch
- **Error flow:** `RuntimeError` (C# exception) during execution -> converted to `StashError` (first-class Stash value) when caught by `try/catch` in Stash code. `AssertionError` extends `RuntimeError` for test reporting

## Language Semantics

Use these rules when writing tests and interpreting behavior:

- **No type coercion on equality:** `5 != "5"`, `0 != false`, `0 != null`
- **Truthiness:** Falsy values are `null`, `false`, `0`, `0.0`, `""`
- **Short-circuit returns operands:** `null || "default"` returns `"default"`, `"a" && "b"` returns `"b"`
- **Reference equality** for dictionaries and struct instances (no value-based `Equals`)
- **Shallow copy** on `dict.merge` - nested structures are shared, not cloned

## Documentation

Detailed docs live in `docs/` - link to these instead of duplicating content. The language spec is **normative law**, not a convenience doc — see *The Specification is the Law* above; every language/runtime behavior change updates it spec-first per `.claude/language-changes.md`.

| Topic                       | File                                         |
| --------------------------- | -------------------------------------------- |
| Full language spec          | `docs/Stash — Language Specification.md`     |
| All namespaces + functions  | `docs/Stash — Standard Library Reference.md` |
| LSP features & architecture | `docs/LSP — Language Server Protocol.md`     |
| DAP features & architecture | `docs/DAP — Debug Adapter Protocol.md`       |
| REPL shell mode             | `docs/Shell — Interactive Shell Mode.md`     |
| TAP test framework          | `docs/TAP — Testing Infrastructure.md`       |
| Templating engine           | `docs/TPL — Templating Engine.md`            |
| Package manager CLI         | `docs/PKG — Package Manager CLI.md`          |
| Package registry            | `docs/Registry — Package Registry.md`        |
| Design specs & analysis     | `docs/specs/`                                |

**`docs/Stash — Standard Library Reference.md` and `docs/Bytecode VM — Instruction Set Reference.md` are generated - do not edit them by hand.**
The stdlib reference is written from `StdlibDefinitions` and `BuiltInErrorRegistry` metadata.
The bytecode instruction reference is written from `Stash.Bytecode/Bytecode/OpCode.cs` XML summaries, category comments, and runtime opcode metadata.
To update generated docs, change the metadata then run:

```bash
dotnet run --project Stash.Docs/
```

Use `--stdlib` or `--bytecode` to regenerate just one document. The generated-reference tests fail if the checked-in files are stale, so CI catches drift automatically.
