# Throws Metadata — Machine-Readable Error Surface for Stdlib and User Code

**Status:** Backlog — Design
**Created:** 2026-05-11
**Author:** Spec Architect
**Origin:** `Error Handling — Architectural Audit and Evolution.md` Section 5, Weakness 1; Phase A3 of the recommended evolution path.
**Scope:** `Stash.Core`, `Stash.Stdlib`, `Stash.Stdlib.Generators`, `Stash.Analysis`, `Stash.Lsp`, `Stash.Format`, `Stash.Check`, `docs/`, `.vscode/extensions/stash-lang/`
**Priority:** High — directly enables typed `catch` to be trustworthy.

---

## 1. Problem Statement

Today there is no machine-readable record of which error types a given function can produce. The stdlib contains 608 throw sites across 35 namespaces; LSP, `stash check`, and the formatter have no way to answer the question "what does `fs.read_text` throw?" Doc comments mention errors prose-style at best — usually not at all — and the source-generator output (`NamespaceFunction`, `BuiltInFunction`) does not surface them.

The user-visible consequence is a confidence problem with the type system the language already advertises. After `Error Type System — Built-in Error Types and Struct Throw Semantics` shipped, users can write:

```stash
try {
    let body = fs.read_text(path);
    let data = json.parse(body);
} catch (IOError e) {
    log.error($"read failed: {e.message}");
} catch (ParseError e) {
    log.error($"bad JSON: {e.message}");
}
```

But the user can only know to write those two catches if they read the source of `fs.read_text` and `json.parse`. There is no hover, no completion, no diagnostic to tell them their `catch (IOError e)` is correct — or that they are missing a clause for `ValueError` that `json.parse` can also throw. They default to `catch (Error e)` and lose the value of typed errors entirely. This is the **Java sprawl trap without Java's tooling support**, as the audit observes.

Concretely, three things are missing:

1. **A declarative way for stdlib functions to record their throw set.** No field on `BuiltInFunction` / `NamespaceFunction`; no attribute on the C# implementation; the source generator never sees throw information.
2. **A declarative way for user functions to record their throw set.** No `@throws` doc-comment convention; parser does not extract one; analyzer does not propagate one.
3. **A way for tooling to consume that data.** Hover, signature help, completions, and an opt-in analyzer rule should all draw from the same authoritative source.

This spec addresses all three. It deliberately stays advisory — Stash does **not** become a checked-exceptions language. Throws metadata is documentation that tooling can read; nothing the runtime enforces, nothing the compiler rejects.

---

## 2. Goals and Anti-Goals

### Goals

1. Every stdlib function can be tagged with its throw set in one obvious place (a C# attribute) — and the metadata flows through the source generator into LSP/analysis without manual duplication.
2. Every user function can document its throw set via a `@throws` doc-comment tag that the lexer/parser preserve into AST metadata.
3. Hover and signature help surface the throw set on every call site that has one.
4. An **opt-in** analysis rule (default off) warns when a `try` block calls a function whose declared throws are not covered by any catch clause.
5. The rollout is incremental — stdlib coverage proceeds in waves by namespace; missing metadata produces no diagnostics and no degraded behaviour.
6. Cross-platform behaviour is invariant — this is metadata, not runtime semantics.

### Anti-Goals

1. **No mandatory checked exceptions.** Function bodies that throw types not listed in `@throws` are never an error. At most, a future opt-in info-level diagnostic (D2 in the audit) might warn.
2. **No new error-handling syntax.** No `throws` keyword in function signatures, no `Result<T, E>` type. Those are separate proposals (Phase C/D of the audit).
3. **No runtime cost.** Throws metadata is consumed at edit time and at `stash check` time only. The VM never reads it.
4. **No breaking changes.** Functions without throws metadata behave exactly as today.

---

## 3. Prior Art Survey

A scripting language for sysadmins should learn from previous error-handling experiments without inheriting their pain. The relevant axis for **this spec specifically** is: how do other languages let a function declare what it throws, and what does tooling do with that information?

### 3.1 Java — Checked Exceptions in the Signature

**Mechanism:** `throws IOException, SQLException` is part of the method signature. Callers must either catch or re-declare; the compiler enforces it.

**Take:** The single most influential idea in this space — Java demonstrated that "what can this throw" is a question worth answering, and that tooling can answer it. The Eclipse/IntelliJ quick-fixes for "wrap in try/catch" and "add throws clause" remain the best in-editor error-handling UX in the industry.

**Reject:** The enforcement model. Java's compulsion produces three failure modes Stash cannot afford:

- **Sprawl.** Every layer re-declares every downstream exception, or wraps to launder the type. Real Java code is studded with `throws Exception` because nobody can keep up.
- **`catch (Exception ignored) {}` becomes endemic.** Compulsion that cannot be satisfied is routed around.
- **Library evolution is brittle.** Adding a new checked exception to a stdlib method is a breaking change for every caller.

For Stash, the lesson is "do the metadata, skip the enforcement." Advisory-only.

### 3.2 Swift — `throws` (Untyped) and `try` Call Marking

**Mechanism:** Functions declare `throws` (since 2014) or `throws(MyError)` (since Swift 5.10, typed throws). Callers must mark every call site with `try` / `try?` / `try!`. Untyped `throws` is the default; typed throws are opt-in and rare.

**Take:** Two ideas worth stealing — (a) typed throws as an _opt-in_ refinement of untyped throws; (b) the rich tooling around `try` (Xcode shows "throwing call" markers, autocompletes `try`, offers "wrap in do/catch"). The progressive disclosure is exactly the right shape: most users see `throws`; advanced users opt into `throws(MyError)`.

**Reject:** The mandatory `try` keyword on every fallible call. The audit's D3 already rules this out — scripting languages historically reject this kind of call-site noise. Stash inherits the metadata idea without the syntactic obligation.

### 3.3 Rust — `Result<T, E>` and `?`

**Mechanism:** Errors are values, not exceptions. Every fallible function returns `Result<T, E>`. The `?` operator threads errors up the call stack.

**Take:** The "errors are values" mental model is excellent; the `?` operator is the best error-propagation syntax in any language. But these are about the **representation** of errors, not the metadata layer this spec addresses. The audit's Phase C, not Phase A.

**Reject for this spec:** Rust's approach assumes a static type system with sum types. Stash is dynamically typed; trying to bolt `Result<T, E>` onto a scripting language produces ceremony without payoff. We continue to use exceptions and add metadata describing them.

### 3.4 Zig — Error Sets, Inferred

**Mechanism:** Error sets (`error{FileNotFound, OutOfMemory}`) are compile-time enumerations. Functions can declare `!T` (returns either a value or any error) and the compiler **infers** the error set from the body, propagating it up. Users can narrow to specific sets when they want to constrain.

**Take:** Inference is the killer feature here — the metadata writes itself. Stash should aspire to this for user functions (Phase D of the audit). Even short of full inference, Zig's principle that "the function's error set is a property derivable from its body" is the right framing for the analyzer.

**Reject:** Compile-time enforcement and the union type for `!T`. Same reasoning as Rust — these need a static type system Stash does not have.

### 3.5 Go — `error` as Conventional Return

**Mechanism:** No language-level error type. Conventionally, fallible functions return `(T, error)`. `errors.Is`, `errors.As` walk wrapped chains. Documentation comments describe errors in prose.

**Take:** `errors.Is` walking a chain is exactly what the audit's Phase B proposes for Stash. Go also demonstrates that **documentation-driven** error contracts work for a large ecosystem — the standard library reliably documents what each function returns as an error, and `golint`/`staticcheck` flag undocumented errors.

**Reject:** The `(val, err)` multi-return pattern. Too verbose for scripting, as the audit notes (D2). But Go's _documentation discipline_ is worth emulating — Stash's `@throws` doc tag is the same idea applied to exceptions.

### 3.6 Python — `:raises:` in Docstrings

**Mechanism:** PEP 257 and the Sphinx/Google/numpy docstring conventions all include a `:raises ExceptionType: description` line. No runtime or static enforcement; purely documentation. IDEs (PyCharm, Pylance) read these and show them on hover.

**Take:** **This is the closest analog to what Stash should ship.** Python is dynamically typed, has exceptions, and has settled on a docstring convention that tooling reads. The convention is widely followed without ever being enforced. Stash's `@throws` tag should look and behave the same way.

**Adopt:** Use this as the model for user-code throws metadata. Match Python's posture: documentation, with tooling-driven payoff but no enforcement.

### 3.7 Ruby — Yard `@raise` Tags

**Mechanism:** The Yard documentation tool defines `@raise [ClassName] description`. RubyMine and Solargraph read these for hover. No language-level mechanism.

**Take:** Confirms the docstring convention from a second dynamically-typed sysadmin-adjacent language. Yard's syntax is essentially what Stash should adopt verbatim.

### 3.8 PowerShell — `[CmdletBinding()]`, `$Error`, and `-ErrorAction`

**Mechanism:** Cmdlets declare metadata via attributes; errors flow into a global `$Error` array; users pass `-ErrorAction Stop` to convert non-terminating errors into terminating ones.

**Take:** The **attribute-on-the-implementation** model maps perfectly to Stash's stdlib. C# attribute on the `BuiltInFunction` provider method, source generator picks it up, no hand-maintained registry. This is what `[StashFn(Throws = ...)]` should look like.

**Reject:** Global `$Error` array (audit calls this out as an anti-pattern — non-thread-safe, debugging pain) and the two-tier terminating/non-terminating model (alien to Stash's exception flow).

### 3.9 Synthesis — What Stash Should Adopt

| Source                                  | What we take                                                                               |
| --------------------------------------- | ------------------------------------------------------------------------------------------ |
| Python `:raises:` / Yard `@raise`       | Doc-comment tag for user functions (`@throws T1, T2`)                                      |
| PowerShell attributes                   | C# attribute for stdlib functions (`[StashFn(Throws = StashErrorTypes.IOError, StashErrorTypesValueError)]`)            |
| Java (the tooling, not the enforcement) | Hover, signature help, code action ("add catch clause for X")                              |
| Swift typed throws                      | The mental model: untyped/throwing is the default; typed is the refinement                 |
| Zig inferred error sets                 | Aspiration for user-function inference (Phase D in audit; this spec stops short)           |
| Go `errors.Is` chain walking            | Already covered by audit Phase B (cause chain); referenced here for diagnostic interaction |

| What we reject                                                  |
| --------------------------------------------------------------- |
| Java mandatory enforcement at the compiler level                |
| Swift mandatory `try` marking at call sites                     |
| Rust/Zig `Result<T,E>` / `!T` sum types (no static type system) |
| Go's `(val, err)` convention (too verbose)                      |
| PowerShell `$Error` global / two-tier errors                    |

The result is a **documentation-driven, advisory-only, attribute-and-tag-based** system. It matches Stash's dynamic-typing, scripting-oriented posture and gives tooling enough rope to make typed catches trustworthy without ever blocking the user.

---

## 4. Design

### 4.1 Two Surfaces, One Metadata Shape

Stash code that can throw lives in two places:

- **Stdlib (C#).** Functions implemented as `BuiltInFunction` delegates in `Stash.Stdlib/BuiltIns/*.cs`, registered via the source generator (`Stash.Stdlib.Generators`).
- **User Stash code.** Functions declared with `fn` in `.stash` files, parsed into AST by `Stash.Core`.

The metadata shape is identical for both:

```
Throws := ordered, distinct list of error-type names (strings)
```

Where each name is one of the built-in error types registered in `StdlibRegistry.Structs` (currently `ValueError`, `TypeError`, `ParseError`, `IndexError`, `IOError`, `NotSupportedError`, `TimeoutError`, `CommandError`) **or** a user-defined struct that the analyzer can resolve as an error type (any struct with a `message: string` field, registered globally — same shape as the built-ins).

Order is preserved for display purposes (hover renders in declaration order). Duplicates are collapsed at metadata-emission time.

### 4.2 User-Code Surface — `@throws` Doc-Comment Tag

#### 4.2.1 Syntax

The `@throws` tag appears inside a doc comment (`///`, the existing Stash convention) attached to a function declaration:

```stash
/// Parse a string as an integer in the given base.
/// @param s the input string
/// @param base the radix (2, 8, 10, or 16)
/// @return the parsed integer
/// @throws ParseError if the string is not a valid integer in the given base
/// @throws ValueError if base is not one of 2, 8, 10, 16
fn parse_int(s: string, base: int = 10) -> int {
    if (base != 2 && base != 8 && base != 10 && base != 16) {
        throw ValueError { message: $"unsupported base: {base}" };
    }
    // ...
}
```

Grammar sketch (informal; doc comments are lexed as trivia and post-processed):

```
DocLine          := "///" Whitespace? DocContent? Newline
DocContent       := SummaryLine | ParamTag | ReturnTag | ThrowsTag | FreeText
ThrowsTag        := "@throws" Whitespace TypeName ( Whitespace Description )?
TypeName         := Identifier
Description      := /[^\n]*/
```

**Multiple `@throws` tags are allowed and additive** — one tag per error type is the recommended style, matching Python `:raises:` and Yard `@raise`. A single tag with comma-separated names is also accepted as a convenience: `@throws ParseError, ValueError if input is malformed`.

The description after the type name is free-form prose. Tooling treats it as a tooltip-friendly explanation of _when_ the throw fires. It is preserved verbatim (after whitespace normalisation) in the metadata.

#### 4.2.2 Where `@throws` Is Allowed

- Top-level `fn` declarations.
- Methods inside `extend` blocks.
- Lambda expressions assigned to a `const` / `let` binding that has a doc comment immediately preceding it.

`@throws` on a non-function declaration (struct, enum, const) is a soft warning (SA0166, info-level): "@throws tag has no effect outside a function declaration." Treating it as an error would be hostile to copy-paste.

#### 4.2.3 AST and Parsing Impact

- **No new AST node type.** The existing `FunctionDecl` AST node already carries a `Documentation: string?` property containing the joined doc-comment lines (see how `NamespaceFunction.Documentation` is consumed today).
- **New field:** add `Throws: ThrowsEntry[]?` to `FunctionDecl` where `ThrowsEntry := record(TypeName: string, Description: string?, Span: SourceSpan)`. Populated during parsing as a structured sibling of `Documentation`.
- The lexer continues to emit doc-comment tokens as trivia. A new helper `DocCommentMetadata.Extract(string raw) -> DocCommentMetadata` in `Stash.Core/Parsing` parses the raw text into structured pieces (summary, `@param`, `@return`, `@throws`). The parser calls this helper when building `FunctionDecl` and stores the result. `Documentation` retains its current shape (joined prose).
- **No semantic dependency.** A missing or malformed `@throws` tag is never a parse error — at worst it produces an info-level diagnostic from the analyzer (SA0166).

#### 4.2.4 Resolution

The analyzer resolves each `TypeName` in `@throws` against:

1. The built-in error structs registered in `StdlibRegistry.Structs`.
2. User-declared structs in scope at the function's declaration site that have a `message: string` field (the **error-shape** convention).

If a `TypeName` resolves to a struct without a `message: string` field, the analyzer emits **SA0167** (info): "'X' is declared in `@throws` but is not an error type (no `message: string` field). Did you mean to declare it as one?"

If a `TypeName` does not resolve at all, the analyzer emits **SA0168** (warning): "'X' in @throws is not a known type." This piggybacks on the existing SA0202 (undefined identifier) machinery but is its own code so it can be suppressed independently.

Neither diagnostic blocks compilation.

### 4.3 Stdlib Surface — `[StashFn(Throws = ...)]` Attribute

#### 4.3.1 Attribute Definition

Add `StashFnAttribute` (or extend the existing attribute if one is already used by the source generator — see Section 4.5). Path: `Stash.Stdlib.Attributes/StashFnAttribute.cs` (new project-level namespace if not already present, otherwise existing location):

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class StashFnAttribute : Attribute
{
    /// <summary>
    /// Comma-separated error type names this function may throw.
    /// Names must match an entry in StashErrorTypes or a registered StashStruct
    /// with a message: string field.
    /// </summary>
    public string[]? Throws { get; set; }
}
```

The attribute is applied to the C# method implementing a stdlib built-in:

```csharp
[StashFn(Throws = [StashErrorTypes.IOError, StashErrorTypes.ValueError])]
private static StashValue ReadText(StashValue[] args, IExecutionContext ctx)
{
    // ...
}
```

The comma-separated string is parsed at generation time into `string[]`. Whitespace is trimmed. Order preserved. Duplicates dropped (compile-time warning if duplicates are present in the source).

#### 4.3.2 Doc-Comment Alternative

C# XML doc comments may _also_ declare throws — and the existing `DocCommentParser` is already in the source-generator pipeline. Add `<exception cref="X">description</exception>` extraction:

```csharp
/// <summary>Read a UTF-8 text file.</summary>
/// <exception cref=StashErrorTypes.IOError>if the file does not exist or is unreadable</exception>
/// <exception cref=StashErrorTypes.ValueError>if the path is empty</exception>
[StashFn(Throws = [StashErrorTypes.IOError, StashErrorTypes.ValueError])]   // optional shortcut; redundant with <exception>
private static StashValue ReadText(...) { ... }
```

The source generator considers both sources. The **union** of `<exception cref>` tags and `[StashFn(Throws=...)]` is the function's throw set. If the two disagree, the generator emits a build-time warning ("Throws metadata mismatch between attribute and doc comment"). This lets the team gradually migrate to whichever convention is preferred without breaking either.

`<exception cref="X">` is the dominant C# convention and surfaces correctly in IDEs (including the IDE used for Stash development). Recommendation: use **`<exception>` as the primary form**, attribute as a fallback for methods without doc comments. Decision logged below.

### 4.4 Metadata Carrier — Extending `NamespaceFunction` and `BuiltInFunction`

Add to both records (`Stash.Stdlib/Models/NamespaceFunction.cs` and `Stash.Stdlib/Models/BuiltInFunction.cs`):

```csharp
public record NamespaceFunction(
    string Namespace,
    string Name,
    BuiltInParam[] Parameters,
    string? ReturnType = null,
    bool IsVariadic = false,
    string? Documentation = null,
    DeprecationInfo? Deprecation = null,
    ThrowsEntry[]? Throws = null);   // NEW

public record ThrowsEntry(string TypeName, string? Description);
```

`ThrowsEntry` lives in `Stash.Stdlib/Models/ThrowsEntry.cs` and is shared by stdlib and user-code metadata. `Stash.Core/Parsing/AST/FunctionDecl.cs` references the same record (cross-project dependency from Core to Stdlib is **not** acceptable — duplicate the tiny record in `Stash.Core/Parsing/AST/ThrowsEntry.cs` instead, since Core cannot depend on Stdlib by Layer 0 rules).

The user-code AST and stdlib metadata thus carry **structurally identical but independently-defined** records. The analyzer reads both via a shared interface `IThrowsCarrier`:

```csharp
// Stash.Analysis/Throws/IThrowsCarrier.cs
public interface IThrowsCarrier
{
    IReadOnlyList<ThrowsEntryDto> Throws { get; }
}

public readonly record struct ThrowsEntryDto(string TypeName, string? Description);
```

Adapter shims convert each concrete record to the DTO at the boundary of the analyzer. This keeps the layering clean (Core has no Stdlib dependency) while letting tooling treat both surfaces uniformly.

### 4.5 Source-Generator Updates

`Stash.Stdlib.Generators/StashNamespaceGenerator.cs` and `DocCommentParser.cs` need three new behaviours:

1. **Extract `<exception cref="X">desc</exception>`** during `DocCommentParser.Parse`. Add `ParseThrows(xml) -> ThrowsEntry[]?` that returns `null` when there are no `<exception>` tags (preserving the "no metadata" sentinel). Whitespace is normalised through the same `NormalizeText` helper.
2. **Read the `[StashFn]` attribute** during `StashNamespaceGenerator`'s symbol walk. Parse the `Throws` property; split on `,`; trim. If both sources are present, emit a diagnostic if they disagree; otherwise union.
3. **Emit the metadata into the generated `NamespaceFunction` constructor call.** Existing emission already passes `Documentation:` via named argument; add `Throws:` similarly. When neither source is present, omit the argument (record default of `null`).

`DocCommentParser`'s current public surface is preserved; the new method is additive. The existing `Documentation` string is unchanged — `@return` and `@param` continue to be appended to the prose for hover display. **`@throws` is NOT appended to the prose**, because it is now represented structurally and rendered separately by hover (Section 4.6.1). Avoiding double-display matters.

### 4.6 Tooling Integration

#### 4.6.1 LSP Hover

`Stash.Lsp/Handlers/HoverHandler.cs` (or its current equivalent) formats hover content for a function symbol. Today: signature + summary. New: append a **Throws** section when `Throws` is non-null.

Markdown layout:

```
fn fs.read_text(path: string) -> string

Reads a UTF-8 text file from disk and returns its contents.

**Throws:**
- `IOError` — if the file does not exist or is unreadable
- `ValueError` — if the path is empty
```

The description column is the free-form prose from `<exception>` or `@throws`. Missing description renders as just the type name as an inline code span. Names are emitted as inline code spans for monospace consistency with other type renderings.

#### 4.6.2 LSP Signature Help

Signature help (parameter assistance during a call) already renders the function's `Detail` and `Documentation`. Add a third pane: **Throws.** Same layout as hover. Same fallback (empty section omitted if no metadata).

#### 4.6.3 LSP Completion Detail

Completion items already carry `Detail` (the signature). The `documentation` field is enriched with the throws section using the same renderer as hover. This is the only place users discover the throw set without first writing the call — vital for the "I'm about to write `try { fs.read_text(...) }`, what should I catch?" workflow.

#### 4.6.4 LSP Code Action — "Surround with try/catch (typed)"

Existing wrap-with-try-catch code actions emit a catch-all. With throws metadata, the action gains a typed variant:

```stash
// User cursor on this line, invokes "Surround with try/catch":
let s = fs.read_text(path);

// Existing action emits:
try {
    let s = fs.read_text(path);
} catch (e) {
    /* TODO */
}

// New typed variant emits:
try {
    let s = fs.read_text(path);
} catch (IOError e) {
    /* TODO: handle IOError */
} catch (ValueError e) {
    /* TODO: handle ValueError */
}
```

The user can pick either action from the lightbulb menu. Default ordering: typed first.

#### 4.6.5 Static Analysis — SA0164 (Opt-In)

New rule **SA0164** in `Stash.Analysis/Rules/`: "Function may throw 'X' but no catch clause matches."

- **Default severity:** off. The rule is opt-in via `.stash-check.json` (or equivalent project config) — exactly the posture the audit specifies for Phase A4.
- **Trigger:** a `try` block whose body contains a call to a function with declared throws T1, T2, T3, and whose catch clauses do not cover all of {T1, T2, T3}. "Cover" means either an explicit `catch (Ti e)` clause **or** a catch-all clause `catch (Error e)` / `catch (e)`. A catch-all suppresses the diagnostic entirely.
- **Severity when enabled:** warning. Never error. The audit is explicit on this point.
- **Fallback for missing metadata:** silent. A try-body that calls only functions with no throws metadata produces no diagnostic, even when enabled. Coverage is incremental; missing metadata never punishes the user.
- **Suppression:** standard `// stash-disable-next-line SA0164` comment works.
- **Code action:** "Add catch clause for X" — appends a `catch (X e) { /* TODO */ }` to the existing try.

Companion rule **SA0169** (info, opt-in, off by default): "Catch clause for 'X' but no call in try body throws 'X'." Detects dead catch clauses once metadata is dense enough. Off by default through Phase A; can be promoted to default-on after Phase B coverage exceeds an agreed threshold (~80% of stdlib).

#### 4.6.6 `stash check` CLI

`Stash.Check` honours the same config-driven rule activation. No new CLI flags; users enable SA0164/SA0169 in the project config the same way they enable any other opt-in rule. CI users who want errors instead of warnings use the existing `--max-severity` flag.

#### 4.6.7 Formatter — `Stash.Format`

The formatter must preserve `@throws` tags as it preserves other doc-comment content. A small enhancement: when multiple `@throws` tags reference the same type, the formatter does **not** collapse them (each documents a different condition); when multiple types are listed on one tag (`@throws A, B`), the formatter does not split them. The formatter is conservative — it preserves the user's authorial choice.

A new opt-in rule `doc.normalize-throws` (default off) reorders `@throws` tags alphabetically. Most users will not want this; teams that do can flip the switch.

#### 4.6.8 VS Code Extension

- **Syntax highlighting:** add `@throws` to the doc-comment keyword list in `.vscode/extensions/stash-lang/syntaxes/stash.tmLanguage.json`, matching the existing `@param` / `@return` patterns. Same highlight scope.
- **Snippets:** the existing `fn` snippet gains a `@throws ${1:ErrorType} ${2:description}` line, commented-out by default.
- **TAP test runner / DAP:** no changes. Metadata is invisible at runtime.

#### 4.6.9 Playground

The Monaco language config (`Stash.Playground/wwwroot/js/stash-language.js`) gains `@throws` in its doc-comment keyword list so the playground's syntax highlighting matches the editor. Hover content is already piped through the LSP-equivalent service the playground uses; once the LSP renderer emits the throws section, the playground gets it for free.

### 4.7 Cross-Platform Behaviour

This spec adds metadata only. No runtime, no I/O, no OS-specific paths. Behaviour is invariant across Linux, macOS, and Windows. The only platform-relevant note is documentation hygiene: stdlib functions whose throw set differs by platform (e.g., `process.spawn` on Windows can throw `NotSupportedError` for POSIX-only options) must list **the union** of types thrown on any supported platform, and the description should call out the platform constraint.

Example:

```csharp
/// <summary>Spawn a child process with the given argv.</summary>
/// <exception cref="StashErrorTypes.IOError">if the executable cannot be launched</exception>
/// <exception cref="StashErrorTypes.NotSupportedError">on Windows, if the options include POSIX-only flags (e.g., setuid)</exception>
[StashFn(Throws = [StashErrorTypes.IOError, StashErrorTypes.NotSupportedError])]
```

### 4.8 Interaction with Existing Error Subsystems

| Subsystem                   | Interaction                                                                                                                                                                                                                   |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `StashError` runtime value  | No change. Metadata never touches runtime.                                                                                                                                                                                    |
| `RuntimeError` C# exception | No change. The `ErrorType` string is what metadata names refer to; the constant in `StashErrorTypes` is the source of truth.                                                                                                  |
| `ErrorTypeRegistry`         | No change. The registry already maps type names to subtype sets; metadata-declared names must already be registered to pass SA0168 validation.                                                                                |
| `try`/`catch` semantics     | No change. Catch matching still uses `se.Type` equality.                                                                                                                                                                      |
| Cause chain (audit Phase B) | When SA0164 fires, the diagnostic message should note that wrapping (causes) is invisible to the analyzer — a catch on the wrapped type matches even if the underlying cause is different. Documentation, not analyzer logic. |
| `try expr ?? default`       | The `?? default` form catches everything. SA0164 treats it as a universal catch-all and never fires for an expression wrapped in `try (...) ?? ...`.                                                                          |
| Defer / suppressed errors   | Out of scope. Defer-thrown errors are recorded on `e.suppressed`; metadata does not currently describe them. Possible future extension.                                                                                       |

---

## 5. Stdlib Coverage Strategy

There are roughly 608 throw sites across 35 namespaces. Coverage rolls out in **waves**, prioritized by user-facing frequency:

### Wave 1 — High-Traffic Foundations (MVP)

| Namespace              | Throw sites (approx) | Why first                                          |
| ---------------------- | -------------------- | -------------------------------------------------- |
| `fs`                   | 60+                  | Almost every script reads/writes files             |
| `process` / shell exec | 40+                  | `CommandError`, `IOError` — the core sysadmin path |
| `io`                   | 20+                  | Println-adjacent, stdin reads                      |
| `conv`                 | 30+                  | `ValueError`, `ParseError` — used everywhere       |
| `json`                 | 15+                  | `ParseError` — JSON parsing is universal           |
| `http`                 | 25+                  | `IOError`, `TimeoutError` — network calls          |

Target: full coverage of Wave 1 in a single sprint. **This is the MVP slice — Wave 1 done is the user-facing payoff.** Without these six namespaces, throws metadata is invisible to most users; with them, the headline error-handling experience changes.

### Wave 2 — Common Namespaces

`str`, `arr`, `dict`, `math`, `time`, `path`, `re`, `crypto`, `encoding`, `net` (excluding sockets), `env`. Another ~150 throw sites. Target: second sprint.

### Wave 3 — Specialized Namespaces

`net.tcp` / `net.udp` / `net.http2`, `ssh`, `sftp`, `ini`, `yaml`, `tpl`, `ws`, `buf`, `regex internals`. ~100 throw sites. Target: third sprint.

### Wave 4 — Long Tail

`scheduler`, `term`, `test`, `complete`, `alias`, `pkg`, `config`, `sys`, `args`, `assert`, and remaining utilities. ~100 throw sites. These have lower call frequency; coverage can lag without blocking the user-facing rollout.

### Coverage Tracking

Add a CI check (or one-off audit script in `tools/`) that:

1. Scans every C# `private static StashValue Foo(...)` registered as a `BuiltInFunction` delegate.
2. Greps the method body for `throw new RuntimeError(`.
3. Asserts that either (a) no throws exist or (b) the method has either `[StashFn(Throws=...)]` or `<exception cref>` metadata.

The check is **default-warning, not default-error** during the rollout; flip to error once each wave's namespaces are complete.

### Throw-Set Audit Tool

To make the audit tractable, add a script `tools/audit_throws.stash` (yes — written in Stash itself, dogfooding) that:

1. Runs `roslyn`/grep against `Stash.Stdlib/BuiltIns/*.cs`.
2. For each method, lists the error type constants observed in `RuntimeError` constructors.
3. Compares to the declared metadata.
4. Emits a per-namespace CSV showing coverage percentage and remaining gaps.

Output is committed to `docs/specs/throws-coverage.md` and updated each wave. Concrete artefact users (and reviewers) can point to.

---

## 6. Migration and Rollout

### 6.1 Phase Plan

| Phase                      | Deliverable                                                                        | Default state                                  |
| -------------------------- | ---------------------------------------------------------------------------------- | ---------------------------------------------- |
| **P1 — Plumbing**          | Attribute + AST field + generator + LSP hover rendering                            | Off for users; metadata accepted but unused    |
| **P2 — Wave 1 coverage**   | 6 high-traffic namespaces tagged                                                   | LSP hover shows throws; SA0164 still off       |
| **P3 — User-code support** | `@throws` doc tag parsed; resolution rules; SA0168                                 | SA0168 default-warning                         |
| **P4 — SA0164 (opt-in)**   | Diagnostic + code action                                                           | Off by default; enabled in `.stash-check.json` |
| **P5 — Wave 2+ coverage**  | Remaining 29 namespaces, by descending traffic                                     | Same                                           |
| **P6 — SA0169 (opt-in)**   | Dead-catch diagnostic                                                              | Off by default                                 |
| **P7 — Promotion review**  | Decision on whether to flip SA0164 to default-warning after coverage threshold met | Pending data                                   |

P1 + P2 + P3 constitute the **minimum viable shippable surface**: users get hover, completion, and signature help showing throws for the namespaces they hit daily, plus the ability to document their own functions. SA0164 (P4) is the next-most-valuable increment.

### 6.2 Backwards Compatibility

- Stdlib without metadata: works as today; hover shows no throws section.
- User code without `@throws`: works as today; SA0164 (when enabled) sees an empty throw set and fires no diagnostics.
- `@throws` tag referencing a missing type: SA0168 warning, no compilation failure.
- Existing doc-comment renderers continue to work — the prose `Documentation` string is unchanged; `@throws` is parsed _out of it_ during AST construction so older renderers see the same content (minus the `@throws` lines, which they were not rendering specially anyway).

### 6.3 Documentation Updates

Per `.claude/language-changes.md`:

- **`docs/Stash — Language Specification.md`:** new subsection under Error Handling: "Documenting Throws." Covers `@throws` tag syntax, resolution rules, interaction with SA0164/SA0168.
- **`docs/Stash — Standard Library Reference.md`:** each namespace's function table gains a "Throws" column. The introduction calls out the convention.
- **`docs/LSP — Language Server Protocol.md`:** note that hover/signature help include throws when metadata is present.
- **`docs/specs/throws-coverage.md`:** living document tracking per-namespace coverage.

### 6.4 Example Script

Per the mandatory checklist, add `examples/throws_metadata.stash`:

- A user function with `@throws ParseError` and `@throws ValueError` showing the convention.
- A call site demonstrating the recommended typed catch pattern.
- A comment block showing the hover output a reader would see in VS Code.

---

## 7. Acceptance Criteria

A reviewer should be able to tick each of these from the implementation alone:

### Spec / Documentation

- [ ] `docs/Stash — Language Specification.md` has a "Documenting Throws" subsection covering `@throws` syntax, resolution, and tooling interaction.
- [ ] `docs/Stash — Standard Library Reference.md` introduction documents the throws-column convention; Wave 1 namespaces' tables list throws.
- [ ] `docs/specs/throws-coverage.md` exists and lists coverage status per namespace.

### Plumbing

- [ ] `StashFnAttribute` exists with a `Throws` string property.
- [ ] `DocCommentParser` extracts `<exception cref="X">desc</exception>` into `ThrowsEntry[]`.
- [ ] `StashNamespaceGenerator` reads attribute + doc tags; unions them; emits a build warning on mismatch.
- [ ] `NamespaceFunction` and `BuiltInFunction` carry an optional `Throws` field.
- [ ] `Stash.Core/Parsing/AST/FunctionDecl` carries a `Throws: ThrowsEntry[]?` field populated from `@throws` doc-comment lines.
- [ ] `DocCommentMetadata.Extract` in `Stash.Core` parses `@throws` into structured entries; `@throws` lines do NOT appear in the prose `Documentation` string.

### Stdlib Coverage (MVP — Wave 1)

- [ ] `fs`, `process`, `io`, `conv`, `json`, `http` are fully tagged. Coverage script confirms 100%.
- [ ] All Wave 1 throw sites that use untyped `RuntimeError` are either retyped or excluded with a documented reason.

### Tooling

- [ ] LSP hover renders a `**Throws:**` markdown section when metadata is present, with one bullet per type and description.
- [ ] LSP signature help renders the same section.
- [ ] LSP completion `documentation` field includes the throws section.
- [ ] LSP code action "Surround with try/catch (typed)" emits typed catch clauses for every type in the function's throw set.
- [ ] `Stash.Analysis` defines `SA0164` (opt-in, default off, warning when enabled).
- [ ] `Stash.Analysis` defines `SA0167` (info), `SA0168` (warning), `SA0169` (info, opt-in).
- [ ] `stash check` respects rule activation from project config.
- [ ] `Stash.Format` preserves `@throws` tags through reformatting.
- [ ] VS Code extension syntax highlights `@throws` in doc comments.
- [ ] Playground Monarch grammar highlights `@throws`.

### Tests

- [ ] xUnit: `DocCommentParser_ParsesExceptionTags_ReturnsEntries`
- [ ] xUnit: `DocCommentParser_MultipleExceptionsSameType_PreservedAsSeparateEntries`
- [ ] xUnit: `DocCommentMetadata_ParsesThrowsTag_PopulatesAst`
- [ ] xUnit: `DocCommentMetadata_ParsesCommaSeparatedThrows_SplitsTypes`
- [ ] xUnit: `DocCommentMetadata_DoesNotIncludeThrowsInDocumentationProse`
- [ ] xUnit: `StashFnAttribute_DeclaresThrows_EmittedIntoNamespaceFunction`
- [ ] xUnit: `Generator_AttributeAndDocCommentAgree_NoWarning`
- [ ] xUnit: `Generator_AttributeAndDocCommentDisagree_EmitsWarning`
- [ ] xUnit: `Hover_FunctionWithThrows_RendersThrowsSection`
- [ ] xUnit: `Hover_FunctionWithoutThrows_NoThrowsSection`
- [ ] xUnit: `SignatureHelp_FunctionWithThrows_IncludesSection`
- [ ] xUnit: `SA0164_TryBlockCallsFunctionWithUncoughtThrow_FiresWhenEnabled`
- [ ] xUnit: `SA0164_DefaultOff_DoesNotFireWithoutConfig`
- [ ] xUnit: `SA0164_CatchAllPresent_DoesNotFire`
- [ ] xUnit: `SA0164_MetadataAbsent_DoesNotFire`
- [ ] xUnit: `SA0164_TryExpressionWithDefault_DoesNotFire`
- [ ] xUnit: `SA0167_ThrowsTagReferencesNonErrorStruct_EmitsInfo`
- [ ] xUnit: `SA0168_ThrowsTagReferencesUnknownType_EmitsWarning`
- [ ] xUnit: `SA0169_CatchClauseUnreachable_EmitsInfoWhenEnabled`
- [ ] xUnit: `CodeAction_SurroundWithTryCatchTyped_EmitsAllTypes`
- [ ] xUnit: `Formatter_PreservesThrowsTags_NoReordering`
- [ ] xUnit: `Wave1_AllFunctionsTagged_CoverageCheckPasses`
- [ ] Example script `examples/throws_metadata.stash` runs to completion under `dotnet test`.

---

## 8. Decision Log

### D1: Advisory only, never enforced

**Chosen:** Throws metadata is documentation. SA0164 is opt-in and warning-level when on.
**Rejected:** Mandatory checked exceptions (Java) or mandatory `try` marking (Swift).
**Rationale:** The audit (Section 4.1, 4.2, 5; D3 in the audit's decision log) is unambiguous: mandatory enforcement is exactly what produces sprawl. Stash's value is in being _scriptable_; ceremony is the enemy.
**Risks:** Without enforcement, metadata can drift from reality. Mitigation: Wave 1 coverage script + CI; phase D of the audit (per-function throw inference) is the long-term answer if drift becomes painful.

### D2: Attribute + XML doc comment in stdlib; `@throws` tag in user code

**Chosen:** Two surfaces; the C# attribute mirrors `<exception cref>` for stdlib; `@throws` mirrors Python `:raises:` for user code.
**Rejected:** Single uniform mechanism (e.g., always doc comments, no attribute).
**Rationale:** The source generator already parses XML doc comments — adding `<exception>` extraction is essentially free. The attribute gives a terse, IDE-friendly form for methods without doc comments (common in early development). Two paths agree by union semantics; mismatch is a build-time warning, not an error. User code cannot use attributes — Stash has no decorator syntax — so a doc-comment tag is the only viable form.
**Risks:** Two paths means two places to remember to update. The build-time mismatch warning catches the most likely drift.

### D3: `<exception>` is the primary stdlib form, attribute is shorthand

**Chosen:** Document `<exception cref="X">desc</exception>` as the recommended pattern. Use `[StashFn(Throws=...)]` only when the method has no doc comment.
**Rejected:** Attribute-primary or attribute-only.
**Rationale:** `<exception>` is idiomatic C#; the same metadata serves the C# code's own IDE experience and the Stash generator. Attributes are best when the carrier _is_ runtime metadata; here the carrier is design-time documentation.
**Risks:** Some methods may end up using only the attribute. Build-time warning policy must accept attribute-only as valid (no warning when only one source is present).

### D4: Built-in error types are the closed set for now; user-defined error structs are recognised

**Chosen:** The analyzer recognises a `TypeName` in `@throws` if it resolves to a struct with a `message: string` field. The eight built-in error types qualify automatically.
**Rejected:** Restrict `@throws` to only the eight built-in types.
**Rationale:** Users will define their own error types (`AuthError`, `DnsError`, etc.) — the analyzer cannot pretend otherwise. The "has `message: string`" convention is the simplest structural test; matches the existing `Error Type System` spec's notion of an error struct.
**Risks:** A user might accidentally tag `@throws Foo` where `Foo` is a non-error struct that happens to have `message: string`. SA0167 catches the inverse case (no `message`); the inverse-of-the-inverse is acceptable false-negative behaviour.

### D5: Order-preserving, comma-separable list

**Chosen:** Multiple `@throws` tags allowed; single tag may comma-separate; order preserved in display.
**Rejected:** One-tag-per-type only.
**Rationale:** Both conventions appear in the wild (Python tends to one-per-line; older Java tends to comma-separated in `throws` clauses). Accepting both is a tiny code cost for an outsized usability win.
**Risks:** None significant.

### D6: SA0164 default-off through Phase A

**Chosen:** SA0164 is opt-in. Project config (`.stash-check.json` or equivalent) enables it.
**Rejected:** Default-warning at launch.
**Rationale:** Coverage starts at 0% and grows by waves. Defaulting on at 0% coverage means the rule almost never fires anyway (no metadata = no diagnostic); defaulting on at 50% coverage produces noisy half-coverage warnings. Better to let teams opt in once they see value, and consider promotion after Wave 2 or Wave 3.
**Risks:** Some users never enable it and lose the value. Mitigation: documentation prominence, recipe in the language spec, CI template.

### D7: SA0164 fires only when metadata is present

**Chosen:** A try-body that calls only un-annotated functions produces no SA0164 diagnostic even when the rule is enabled.
**Rejected:** Treat un-annotated functions as "may throw anything" and require a catch-all.
**Rationale:** Punishing users for the stdlib's incomplete coverage is a non-starter. Silent fallback is the correct posture during incremental rollout.
**Risks:** Users may not realise their catch is incomplete because the rule cannot tell them. This is acceptable; the alternative (false positives everywhere) is worse.

### D8: Two structurally-identical `ThrowsEntry` records — one in Core, one in Stdlib

**Chosen:** Duplicate the small record across the Core/Stdlib layer boundary; analyzer normalizes both through a DTO.
**Rejected:** Put `ThrowsEntry` in a shared lower layer that Core can depend on.
**Rationale:** Core is Layer 0; nothing below it exists. Inverting the dependency (Stdlib referencing a Core record) would force Stdlib's parser to know about AST records it has no business owning. The duplicate is two lines.
**Risks:** Drift between the two records. Mitigation: a single xUnit test asserting structural equivalence.

### D9: No `throws` clause in function signatures

**Chosen:** `@throws` lives in doc comments, not signatures. Signatures are unchanged.
**Rejected:** `fn foo(...) throws (IOError) -> int { ... }` (Swift-like).
**Rationale:** Adding to the function signature crosses into "the type system knows about errors," which then begs the question of enforcement and propagation. This is Phase D of the audit, not Phase A. Keeping `@throws` in doc-comment-land is a deliberate signal that this is documentation.
**Risks:** When/if Phase D arrives, `@throws` may become the seed for an inferred signature — at which point the doc-tag becomes the authoritative source. Forward-compatible.

### D10: No structural metadata for individual `throw` sites

**Chosen:** Metadata is per-function. The analyzer does not record which line within a function throws which type.
**Rejected:** Per-line throw-site tracking (would enable richer diagnostics like "this specific throw is undeclared").
**Rationale:** Per-line tracking is what Phase D of the audit is about (inferred throws). This spec stops at the function-level metadata layer.
**Risks:** None at this phase.

---

## 9. Open Questions for the User

Resolve these before promotion to `1-todo/`:

1. **Attribute primary vs `<exception>` primary?** D3 picks `<exception>`-primary. Is that the right call given how stdlib team members typically write C#? If the team rarely writes XML doc comments today, attribute-primary may be more pragmatic.
Answer: Yes, we use C#'s XML documentation as much as possible since it's tried and tested and proven to be the correct approach.

2. **Comma-separated vs string array in `[StashFn(Throws=...)]`?** D5 picks comma-separated. Acceptable, or should the attribute take a `string[]` for IDE refactoring tool support?
Answer: We should not use literal strings at all, we should always reference "StashErrorTypes.*" to avoid drift and to aid refactoring.

3. **Should `@throws` be allowed on lambdas?** Section 4.2.2 says yes (for `const`/`let` bindings with a doc comment immediately preceding). Is that worth the parser complexity, or should lambdas be excluded entirely in v1?
Answer: Yes, lambdas are still functions so they should be allowed to document their exceptions.

4. **SA0168 severity for unresolved types — warning or info?** Currently warning (Section 4.2.4). A warning becomes noise during early adoption when users may have typos. Should it be info-level during Phase P3 and promoted later?
Answer: Info at first to verify, then promote to warning once the implementation is complete.

5. **`<exception>` for AOT-only quirks.** If a function throws different types depending on whether it's running in the AOT CLI vs the JIT LSP host (e.g., reflection-related failures), how should the union policy describe that? Recommendation: list the AOT-target type and document the JIT-only type in prose. Confirm or revise.
Answer: I don't understand the question, do we have different behaviour in the standard library depending on AOT or JIT LSP?

6. **Coverage thresholds for promotion.** Section 6.1 P7 references a coverage threshold (~80% of stdlib) before considering flipping SA0164 to default-on. Is 80% the right bar, or should the bar be higher (e.g., Wave 4 complete)?
Answer: Hard to measure exact percentage, let's say we flip SA0164 when wave 4 is complete instead.

7. **`task.parallel` and similar fan-out APIs.** The audit Section 4.6 suggests these should return per-task `Result`-style outcomes. That's a separate spec, but it interacts with throws metadata: do we declare `task.parallel` as `throws` (because individual tasks may throw) or as non-throwing because failures are returned as values? Recommendation: non-throwing, document in prose. Confirm.
Answer: non-throwing, document in prose.

8. **Should `@throws` description text support inline markdown?** Python's `:raises:` allows reST; Yard allows markdown. Recommendation: treat as plain text with backtick code spans only, to keep the hover renderer simple. Confirm.
Answer: Keep it as plain text with backtick code.

9. **Plumbing-phase scope.** Is the team comfortable shipping P1 (plumbing) with zero stdlib coverage as a separate PR, or should the MVP slice insist on P1 + P2 (Wave 1 coverage) shipping together? Recommendation: separate — P1 is a small, low-risk infrastructure PR; P2 is a large mechanical-coverage PR. Confirm.
Answer: Separate steps, separate commits. We do it incrementally.

10. **`@throws` ordering and the formatter.** Section 4.6.7 leaves alphabetical reordering as opt-in. Is the default (preserve author order) the right call, or should the formatter normalise to declaration order across the codebase by default for consistency?
Answer: Preserve author order.
