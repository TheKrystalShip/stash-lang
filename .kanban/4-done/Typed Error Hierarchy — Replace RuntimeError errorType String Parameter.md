# Typed Error Hierarchy — Replace `RuntimeError` `errorType` String Parameter

| Field         | Value                                                                  |
| ------------- | ---------------------------------------------------------------------- |
| **Status**    | Backlog                                                                |
| **Created**   | 2026-05-12                                                             |
| **Author**    | Spec Architect (with user)                                             |
| **Owner**     | TBD                                                                    |
| **Tracking**  | _none yet_                                                             |
| **Effort**    | Large — touches Stdlib (629 sites), VM (175), Core (84), CLI/Analysis (47), source generator, throws metadata, analyzer, docs |

---

## 1. Problem Statement

Today, every site in the codebase that raises a runtime error from C# does so via:

```csharp
throw new RuntimeError("config.write: " + e.Message, errorType: StashErrorTypes.IOError);
```

The `errorType:` parameter is a stringly-typed tag whose value must (almost always) match a constant in `StashErrorTypes`. This pattern has accreted three independent registries that must be kept in lock-step:

1. **`StashErrorTypes`** — string constants (`Stash.Core/Runtime/ErrorTypes.cs`).
2. **`ErrorTypeRegistry._subtypes`** — `HashSet<string>` for `is`/`catch` matching (`Stash.Core/Runtime/ErrorTypeRegistry.cs`).
3. **`SemanticValidator._builtInErrorTypes`** — analyzer's view of built-in error names.

Plus several adjacent systems are downstream consumers of the same string:

- **Throws-metadata source generator** (`Stash.Stdlib.Generators`) parses `<exception cref="StashErrorTypes.IOError">` doc tags by extracting the trailing identifier as a string.
- **Built-in struct registration** in `GlobalBuiltIns.cs` declares `IOError`, `TypeError`, ... as built-in struct types so users can write `catch (e: IOError)`.
- **`StdlibRegistry.Types.cs`** has `TypeDescriptions` for each error type for LSP.

The result is **massive cognitive overhead and documentation drift potential**:

- Adding a new built-in error subtype requires touching 5+ files.
- The `errorType:` argument can silently be misspelled, omitted, or pointed at the wrong constant.
- Tests use `Assert.Equal(StashErrorTypes.X, ex.ErrorType)` — they don't check that the right C# type was raised, just that the right string was tagged on a generic `RuntimeError`.
- Code reviewers cannot tell at a glance whether the right error subtype is being raised — they have to read the parameter list.

The stdlib already proved that **self-registering, source-generator-driven metadata** works (see "Stdlib Built-in Definition System — Source Generator" in `4-done/`). This spec applies the same pattern to runtime errors.

## 2. Goals & Non-Goals

### Goals

- **Eliminate the `errorType:` string parameter** from `RuntimeError` for built-in errors. Code becomes:
  ```csharp
  throw new IOError("config.write: " + e.Message);
  ```
- **Single source of truth per error type** — one C# class declaration that:
  - Exposes the canonical Stash-facing name (e.g. `"IOError"`).
  - Carries any typed properties (e.g. `CommandError.ExitCode`).
  - Auto-registers as a built-in struct type for `catch (e: IOError)`.
  - Auto-populates `ErrorTypeRegistry._subtypes` and analyzer sets.
- **Throws-metadata stays consistent automatically** — the source generator reads `<exception cref="IOError">` and looks up the canonical name from the type, no string parsing.
- **AOT compatibility preserved** — Stash.Cli, Stash.Check, Stash.Format use Native AOT. No reflection-based discovery.
- **Backward-compatible runtime semantics** for Stash code:
  - `throw { type: "Foo", message: "..." }` from Stash still works (user-defined error names).
  - `catch (e: IOError)` still matches errors raised from C# as `new IOError(...)`.
  - `e.type` from Stash still returns the canonical string name.

### Non-Goals

- **Not introducing a typed-throw syntax for Stash code** in this spec (e.g. `throw IOError("msg")` from Stash). That's a sensible follow-up but adds parser/compiler/runtime work that should land separately.
- **Not redesigning `StashError`** (the first-class Stash value). It still has `message`, `type`, `properties`, `cause`, etc.
- **Not changing `is`/`catch` semantics from a user perspective.** The implementation may accelerate the dispatch but the matching rules don't change.

## 3. Design Decisions

| Decision                                | Choice                                                                                              | Rationale                                                                                                   |
| --------------------------------------- | --------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| **Migration scope**                     | Full — remove `errorType:` parameter. Old form becomes a compile error.                              | Half-migration leaves the registry drift problem in place.                                                  |
| **C# class shape**                      | Top-level classes in namespace `Stash.Runtime.Errors`. One file per type.                            | Discoverability via IntelliSense (`using Stash.Runtime.Errors;`). Nested-class form rejected as too verbose at use sites. |
| **User-defined errors**                 | Two parallel hierarchies. Built-ins are real C# subclasses; user-thrown errors flow through `UserRuntimeError : RuntimeError` carrying the string name. | Built-ins gain type identity; user code retains arbitrary-name flexibility. VM unifies both at StashError construction. |
| **Self-registration**                   | Source generator scans `[StashError]`-attributed classes, emits a static registry table.            | AOT-friendly, mirrors the proven stdlib generator pattern.                                                  |
| **`is`/`catch` matching**               | Type-identity primary, name fallback for user-thrown collision cases.                                | Identity is fast. Name fallback preserves user-code semantics (`throw { type: "IOError" }` still matches `catch (e: IOError)`). |
| **Throws-metadata doc tags**            | `<exception cref="IOError">` resolves to canonical name via the C# type. Drop `[StashFn(Throws=...)]` attribute form. | Single source of truth. Roslyn tells the generator the resolved type symbol; no string parsing needed.       |
| **Typed properties (`CommandError`)**   | Subclass exposes typed C# properties; base `Properties` dict is auto-populated via virtual `GetProperties()`. | Type safety for C# call sites; dict is built once at the StashError boundary.                               |
| **Delivery**                            | Single spec, multi-phase implementation. See §10.                                                   | Keeps related changes coherent; phases keep PRs reviewable.                                                 |

## 4. C# Hierarchy

### 4.1 Base class

`RuntimeError` keeps its existing role as the catch-all base, but loses the `errorType` constructor parameter:

```csharp
namespace Stash.Runtime;

public class RuntimeError : Exception
{
    public SourceSpan? Span { get; }
    public Dictionary<string, object?>? Properties { get; init; }
    public List<StashError>? SuppressedErrors { get; init; }
    public List<StackFrame>? CallStack { get; set; }

    public RuntimeError(string message, SourceSpan? span = null) : base(message)
    {
        Span = span;
    }

    // For subclasses to populate the dict that flows into StashError.Properties.
    // Default: returns Properties as-is. Typed subclasses override.
    protected internal virtual Dictionary<string, object?>? GetProperties() => Properties;
}
```

Notes:

- **There is no `CanonicalName` property and no string field for the error type.** The Stash-facing name is **always** derived from `GetType().Name`. The C# class name *is* the canonical name. Period.
- The legacy `ErrorType` string property is **removed**. Code that read `ex.ErrorType` reads `ex.GetType().Name` instead (or, more idiomatically, switches to `if (ex is IOError ioe) { ... }`).
- `RuntimeError` itself is no longer constructible with an arbitrary string error type. Direct `new RuntimeError("msg")` reports as `"RuntimeError"` (its class name). This is intentional — there is no longer a back door for arbitrary names from C# code.
- For subclasses where the desired Stash-facing name **must** differ from the C# class name (e.g. avoiding a name clash with `System.IO.IOException`), the source generator emits a compile-time mapping via the `[StashError(Name = "...")]` attribute property. The mapping is consumed at the `StashError` boundary; the C# class never carries the string itself.

### 4.2 Built-in error subclasses

Each built-in lives in its own file under `Stash.Core/Runtime/Errors/`:

```csharp
// Stash.Core/Runtime/Errors/IOError.cs
namespace Stash.Runtime.Errors;

[StashError]
public sealed class IOError : RuntimeError
{
    public IOError(string message, SourceSpan? span = null) : base(message, span) {}
}
```

That's it. No `CanonicalName`, no string constant, no registry entry to hand-maintain. The Stash-facing name `"IOError"` is `typeof(IOError).Name`, resolved once and cached by the generator.

Errors with typed payload (e.g. `CommandError`) declare typed properties:

```csharp
// Stash.Core/Runtime/Errors/CommandError.cs
namespace Stash.Runtime.Errors;

[StashError(Properties = new[] { "exitCode", "stderr", "stdout", "command" })]
public sealed class CommandError : RuntimeError
{
    public long ExitCode { get; }
    public string? Stderr { get; }
    public string? Stdout { get; }
    public string? Command { get; }

    public CommandError(string message, long exitCode, string? stderr, string? stdout, string? command, SourceSpan? span = null)
        : base(message, span)
    {
        ExitCode = exitCode;
        Stderr = stderr;
        Stdout = stdout;
        Command = command;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["exitCode"] = ExitCode,
        ["stderr"] = Stderr,
        ["stdout"] = Stdout,
        ["command"] = Command,
    };
}
```

### 4.3 User-thrown errors

```csharp
// Stash.Core/Runtime/Errors/UserRuntimeError.cs
namespace Stash.Runtime.Errors;

/// <summary>
/// Carries an arbitrary user-supplied error type name from Stash code
/// (e.g. <c>throw { type: "MyError", message: "..." }</c>).
/// Never raised from built-in C# code.
/// </summary>
public sealed class UserRuntimeError : RuntimeError
{
    /// <summary>
    /// The user-supplied type name. This is the **only** error class where a string name is
    /// stored in a field, because the name comes from runtime data (a Stash dict literal),
    /// not from a C# class identity. The C#→Stash conversion uses this directly instead of
    /// <c>GetType().Name</c>.
    /// </summary>
    public string UserTypeName { get; }

    public UserRuntimeError(string typeName, string message, SourceSpan? span = null)
        : base(message, span)
    {
        UserTypeName = typeName;
    }
}
```

The `Throw` opcode handler that processes `throw { type: "X", ... }` constructs a `UserRuntimeError`. If `type` matches a built-in error name, see §6.3 for the resolution rule.

## 5. The `[StashError]` Attribute & Source Generator

### 5.1 Attribute

```csharp
namespace Stash.Stdlib.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StashErrorAttribute : Attribute
{
    /// <summary>
    /// Optional override of the Stash-facing name. **Defaults to the C# class name** —
    /// supply this only when the desired Stash name cannot be the C# class name (e.g. to
    /// avoid a clash with a BCL type, or to keep a legacy name during a class rename).
    /// Use sparingly; the whole point of this design is that the C# class name is the name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Names of typed properties exposed to Stash code via <c>e.&lt;property&gt;</c>.
    /// Used by the source generator to emit struct field metadata.
    /// </summary>
    public string[]? Properties { get; init; }
}
```

> **Convention:** the `Name` property exists as an escape hatch only. A reviewer seeing `[StashError(Name = "X")]` should treat it as a code smell that needs justification in a comment. The default — and overwhelmingly common — form is the bare `[StashError]`.

### 5.2 Generator output

The new generator (`StashErrorRegistryGenerator`, sibling of `StashNamespaceGenerator`) scans every assembly for `[StashError]`-decorated classes and emits a single registry:

```csharp
// Generated: Stash.Core/Runtime/Errors/__BuiltInErrors.g.cs
namespace Stash.Runtime.Errors;

public static partial class BuiltInErrorRegistry
{
    // Generator emits one entry per [StashError] class. The string keys come from
    // typeof(T).Name (or the [StashError(Name = "...")] override when present).
    // No hand-maintained string lists anywhere.
    private static readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal)
    {
        [nameof(IOError)] = typeof(IOError),
        [nameof(TypeError)] = typeof(TypeError),
        [nameof(CommandError)] = typeof(CommandError),
        // ... etc.
    };

    private static readonly Dictionary<Type, string> _byType = new()
    {
        [typeof(IOError)] = nameof(IOError),
        // ... etc.
    };

    private static readonly Dictionary<string, BuiltInErrorMetadata> _metadata = new()
    {
        [nameof(CommandError)] = new BuiltInErrorMetadata(
            name: nameof(CommandError),
            clrType: typeof(CommandError),
            properties: new[] { "exitCode", "stderr", "stdout", "command" }),
        // ... etc.
    };

    public static IReadOnlyDictionary<string, Type> ByName => _byName;
    public static IReadOnlyDictionary<Type, string> ByType => _byType;
    public static IReadOnlyDictionary<string, BuiltInErrorMetadata> Metadata => _metadata;

    public static bool IsBuiltInName(string name) => _byName.ContainsKey(name);
    public static bool TryGetName(Type clrType, out string name) => _byType.TryGetValue(clrType, out name!);

    /// <summary>Canonical Stash name for any RuntimeError. Handles <see cref="UserRuntimeError"/> specially.</summary>
    public static string NameOf(RuntimeError ex) => ex switch
    {
        UserRuntimeError u => u.UserTypeName,
        _ => _byType.TryGetValue(ex.GetType(), out var n) ? n : ex.GetType().Name,
    };
}
```

The `NameOf` helper is the **single point** at which a `RuntimeError` is reduced to a string for `StashError.Type`. Every C#→Stash boundary calls it; nothing else hard-codes a name.

This **replaces** `StashErrorTypes`, `ErrorTypeRegistry._subtypes`, and `SemanticValidator._builtInErrorTypes`. All three become `BuiltInErrorRegistry.ByName.ContainsKey(...)` lookups (or are deleted entirely).

### 5.3 Compile-time diagnostics

The generator emits diagnostics for:

- `STSE001` — `[StashError]` class not in the `Stash.Runtime.Errors` namespace.
- `STSE002` — `[StashError]` class not derived from `RuntimeError`.
- `STSE003` — `[StashError]` class declares any property/field/constant named `CanonicalName`, `ErrorType`, or `TypeName`. (Catches anyone tempted to reintroduce a redundant string accessor — the canonical name is `GetType().Name`, full stop.)
- `STSE004` — Duplicate canonical name across two `[StashError]` classes (only possible when `[StashError(Name = "...")]` overrides collide with a class name).
- `STSE005` — `[StashError]` class declares `Properties` in the attribute that don't match keys returned by `GetProperties()` (best-effort; only fires when both can be statically inspected).

## 6. Runtime Semantics

### 6.1 C# → `StashError` conversion

The eight call sites in the VM that build `StashError` from a `RuntimeError` change:

```csharp
// Before
new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties)

// After
new StashError(ex.Message, BuiltInErrorRegistry.NameOf(ex), null, ex.GetProperties(), builtInClrType: GetBuiltInType(ex))
```

Where `GetBuiltInType` returns `ex.GetType()` if `BuiltInErrorRegistry.ByType` contains it, else `null` (notably null for `UserRuntimeError`). This C# Type reference enables fast identity dispatch in §6.2.

`StashError` gains an optional `BuiltInClrType` field (nullable `Type`). It is **not exposed to Stash code** — it's a runtime-only optimization marker.

### 6.2 `is` / `catch` matching

The compiler resolves `T` in `catch (e: T)` and `e is T` at compile time. Three cases:

| `T` resolves to                                       | Dispatch                                                                                    |
| ----------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| **Base `Error` symbol**                               | Match any `StashError` (existing behavior).                                                 |
| **Built-in error struct (`IOError`, `CommandError`, …)** | Compiler emits a typed-match opcode operand referring to the CLR type. VM checks: `error.BuiltInClrType == T.ClrType` first; if `error.BuiltInClrType` is null, fall back to `error.Type == T.CanonicalName`. |
| **User struct symbol** (anything else)                | Name-based match: `error.Type == T.Name`.                                                   |

The fallback path in the second case is what allows `throw { type: "IOError", ... }` from Stash code to still be caught by `catch (e: IOError)`.

### 6.3 User throws colliding with built-in names

When `throw { type: "X", message: "..." }` is executed:

- If `BuiltInErrorRegistry.IsBuiltInName("X")` is true: the runtime constructs `UserRuntimeError("X", message)`. `BuiltInClrType` is **not** set (it came from user code, not C# instantiation). The name-fallback rule in §6.2 ensures matching still works.
- Otherwise: same `UserRuntimeError("X", message)` with no special handling.

A new analyzer rule **`SA0860`** (Warning, category Errors) fires when the static analyzer sees a literal `throw { type: "<built-in name>", ... }` and suggests the user prefer the built-in form. The rule does **not** fire for dynamic `type` expressions (only string-literal tags).

> **Future work:** Once Stash gains a typed-throw syntax (e.g. `throw IOError("msg")`), `SA0860` will recommend that form instead of the dict literal.

### 6.4 Properties

`StashError.Properties` continues to be populated from `RuntimeError.GetProperties()`. The behavior change is purely how the dict gets built — typed subclasses materialize it from typed C# properties; user errors and untyped subclasses pass through whatever the constructor stored.

## 7. Throws-Metadata Source Generator Changes

### 7.1 Doc-tag form

```csharp
/// <exception cref="IOError">if the file cannot be read</exception>
[StashFn]
private static StashValue Read(string path) { ... }
```

The generator already calls `DocCommentParser.Parse()`. Today it extracts `IOError` from `StashErrorTypes.IOError` by string-trimming. The new logic uses Roslyn's symbol resolution:

1. The `<exception>` `cref` resolves to an `INamedTypeSymbol`.
2. Verify the symbol has `[StashError]` (or is `RuntimeError` for the unconstrained case).
3. Look up the canonical name via the attribute's `Name` property, or fall back to the type's class name.

This drops the brittle string-trimming entirely.

### 7.2 Attribute form

```csharp
[StashFn(Throws = new[] { typeof(IOError), typeof(ParseError) })]
```

Replaces the old `Throws = new[] { "IOError", "ParseError" }` form. The `Throws` property type changes from `string[]` to `Type[]`. The generator validates each `Type` has `[StashError]`.

### 7.3 Mismatch diagnostics

`STSG010` (throws-metadata mismatch) now compares `Type[]` sets instead of string sets. The error message reports class names, not strings.

## 8. Migration Map

### 8.1 Files affected (categorical)

| Layer                | Count      | Migration                                                                                                  |
| -------------------- | ---------- | ---------------------------------------------------------------------------------------------------------- |
| **Stash.Stdlib**     | 629 sites  | `new RuntimeError("msg", errorType: StashErrorTypes.X)` → `new X("msg")`. Mechanical sed-with-review.       |
| **Stash.Bytecode**   | 175 sites  | Same mechanical replacement. Plus eight `new StashError(..., ex.ErrorType ?? "RuntimeError", ...)` sites change to use `ex.CanonicalName` and `ex.GetProperties()`. |
| **Stash.Core**       | 84 sites   | Same. `RuntimeError` itself loses the `errorType` ctor param.                                              |
| **Stash.Cli + Analysis** | 47 sites | Same.                                                                                                      |
| **Stash.Tests**      | ~30 `Assert.Equal(StashErrorTypes.X, ex.ErrorType)` sites | Become `Assert.IsType<X>(ex)` (stronger assertion).                |
| **Stash.Stdlib.Generators** | 2 files | `StashNamespaceGenerator.cs`, `DocCommentParser.cs` — switch from string-extraction to symbol resolution. Add new `StashErrorRegistryGenerator`. |
| **Stash.Analysis**   | 3 files    | `SemanticValidator._builtInErrorTypes` set deleted, replaced with `BuiltInErrorRegistry.IsBuiltInName(...)`. `UnreachableCatchRule`, `UncaughtDeclaredThrowRule` consume canonical names from the registry. |
| **GlobalBuiltIns / StdlibRegistry.Types** | 1 file each | Built-in struct registrations for `IOError`, `TypeError`, etc. become loop-driven from `BuiltInErrorRegistry.Metadata`. |

### 8.2 What gets deleted

- `Stash.Core/Runtime/ErrorTypes.cs` (entire `StashErrorTypes` constants class).
- `Stash.Core/Runtime/ErrorTypeRegistry.cs._subtypes` field (the `Matches` method becomes a thin wrapper over `BuiltInErrorRegistry`).
- `SemanticValidator._builtInErrorTypes` (the field).
- The `Throws = new[] { "string", ... }` attribute overload on `StashFnAttribute`.

### 8.3 What stays

- `RuntimeError` (`ErrorType` property removed; canonical name is `BuiltInErrorRegistry.NameOf(ex)` which returns `ex.GetType().Name` for built-ins and the user-supplied string for `UserRuntimeError`).
- `AssertionError : RuntimeError` (existing subclass — gains `[StashError]` for consistency).
- `StashError` runtime value — gains a non-Stash-visible `BuiltInClrType` field.
- `ErrorTypeRegistry.BaseTypeName = "Error"` and `Matches` (delegating to the new registry).

## 9. Interaction with Existing Features

### 9.1 Streaming command cancellation

Per repo memory, `CancellationError` is converted to `TimeoutError` at the `task.timeout` boundary by re-tagging via:

```csharp
catch (AggregateException ae) when (ae.InnerException is RuntimeError re && re.ErrorType == StashErrorTypes.CancellationError)
```

This becomes:

```csharp
catch (AggregateException ae) when (ae.InnerException is CancellationError ce)
```

Stronger and clearer. The throw-site in `VirtualMachine.Dispatch.cs` becomes `throw new CancellationError("Operation cancelled.") { SuppressedErrors = suppressed }`.

### 9.2 Strict-mode `CommandError`

`process.exec` strict-mode failure currently uses:

```csharp
throw new RuntimeError(msg, errorType: StashErrorTypes.CommandError)
{
    Properties = new() { ["exitCode"] = code, ["stderr"] = stderr, ["stdout"] = "", ["command"] = cmd }
};
```

Becomes the typed constructor:

```csharp
throw new CommandError(msg, exitCode: code, stderr: stderr, stdout: "", command: cmd);
```

The dict materializes lazily via `GetProperties()` at the StashError boundary. **Zero behavior change** for Stash code reading `e.exitCode`, `e.stderr`, etc.

### 9.3 LSP / DAP impact

- LSP hover for built-in error structs (`IOError`) reads from `BuiltInErrorRegistry.Metadata` instead of the hand-maintained `StdlibRegistry.Types` table.
- Throws-metadata in hover/completion/signature-help unaffected at the consumer layer — it still receives `ThrowsEntry(string ErrorType, string? Description)` records. Only the upstream generator-side sourcing changes.

### 9.4 Bytecode format

The typed-match opcode in §6.2 may need a new operand encoding (CLR-type token vs. name token). Two implementation options:

1. **Reuse name-based opcode**, but the catch frame caches a CLR-type reference for fast comparison after the first miss. Format-compatible with v3 chunks.
2. **New opcode `OpCode.IsBuiltInError`** with a registry-index operand. Bumps `.stashc` `FormatVersion`.

> **Decision deferred to Phase B implementation.** Likely (1) — caching keeps the format stable.

### 9.5 Error tests in test suite

~30 sites currently do:

```csharp
var ex = Assert.Throws<RuntimeError>(...);
Assert.Equal(StashErrorTypes.IOError, ex.ErrorType);
```

Migrate to:

```csharp
Assert.Throws<IOError>(...);
```

This is a strict improvement — the test now verifies the actual C# type was raised, not just that *some* `RuntimeError` happened to be tagged with the right string.

## 10. Phased Delivery

| Phase   | Scope                                                                                                                                                                                                                                                                              | Reviewable?          |
| ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------- |
| **A**   | Add `[StashError]` attribute, write `StashErrorRegistryGenerator`, declare all 12 built-in subclasses + `UserRuntimeError`. Add `CanonicalName` virtual property to `RuntimeError`. Generator emits `BuiltInErrorRegistry`. **No call-site changes yet** — both old and new forms coexist temporarily. | Yes — small, focused. |
| **B**   | Migrate all Stdlib call sites (629). Stdlib tests updated to `Assert.Throws<IOError>` form. `StashErrorTypes` constants kept temporarily (still used by VM/Core).                                                                                                                   | Yes — large but mechanical, namespace-by-namespace. |
| **C**   | Migrate VM (175) + Core (84) + CLI/Analysis (47) call sites. Update the eight `new StashError(..., ex.ErrorType ?? ..., ex.Properties)` boundaries to use `CanonicalName` + `GetProperties()`. Add `BuiltInClrType` to `StashError` and wire typed-match dispatch in the VM.        | Yes — largest PR, but the Stdlib migration de-risks the pattern. |
| **D**   | Update throws-metadata generator: doc-tag resolution via Roslyn symbols, `[StashFn(Throws = typeof(...))]` attribute form, mismatch diagnostics. Update `SemanticValidator._builtInErrorTypes`, `UnreachableCatchRule`, `UncaughtDeclaredThrowRule` to consume `BuiltInErrorRegistry`. | Yes.                 |
| **E**   | Delete `StashErrorTypes` class, `errorType:` parameter from `RuntimeError`, the old `Throws = new[] { string }` attribute form. Add `SA0860` (user-throw collision warning). Remove `ErrorTypeRegistry._subtypes`. Update language spec, stdlib reference, CHANGELOG.               | Yes — pure deletions + docs. |

Each phase is a self-contained PR. Phase A unblocks B and is small. Phases B and C are large but mechanical. Phase D is independent of B/C call-site count. Phase E is the cleanup that removes the deprecated forms.

> **Important:** Between phases A and E, the codebase has both forms. The generator should emit a `[Obsolete]` attribute on the old `errorType:` parameter starting in Phase A so reviewers can see which sites still need migration.

## 11. Test Plan

| Layer           | Tests                                                                                                                                                                                                                                                                  |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Generator**   | `BuiltInErrorRegistryGeneratorTests` — verify all 12 built-ins are registered, `Properties` metadata is correct, STSE001-005 diagnostics fire on misuse.                                                                                                                |
| **Runtime**     | `BuiltInErrorRegistryTests` — `IsBuiltInName`, `ByType`, `ByName` lookups. `RuntimeErrorTests` — `CanonicalName` defaults, `GetProperties()` for typed subclasses.                                                                                                       |
| **VM**          | `CatchMatchTests` — `catch (e: IOError)` matches `new IOError(...)` via type identity. Same matches `throw { type: "IOError", ... }` via name fallback. `e is IOError` works for both. Assert `BuiltInClrType` is set for C#-raised, null for user-raised.              |
| **Stdlib**      | All existing `Assert.Equal(StashErrorTypes.X, ex.ErrorType)` rewrites to `Assert.Throws<X>` (or `Assert.IsType<X>` where the throw is wrapped). Verify `e.exitCode` etc. still readable for `CommandError`.                                                              |
| **Analyzer**    | `SA0860Tests` — fires on `throw { type: "IOError", ... }`, doesn't fire on `throw { type: "MyError", ... }`. `UnreachableCatchRule` and `UncaughtDeclaredThrowRule` regression suite.                                                                                   |
| **End-to-end**  | One Stash test per built-in error: `try { c.x() } catch (e: X) { ... }` works for all 12 types. `e.type == "IOError"` etc. still returns the canonical string.                                                                                                          |

## 12. Cross-Platform Notes

No platform-specific concerns. Error type names and class hierarchies are pure language/runtime constructs.

## 13. Documentation Updates

- `docs/Stash — Language Specification.md` — error types section: list the 12 built-ins, explain that `e.type` returns canonical names, document the `SA0860` warning.
- `docs/Stash — Standard Library Reference.md` — error types table is generated/derived from `BuiltInErrorRegistry.Metadata` (consider a compile-time check).
- `.github/instructions/stdlib.instructions.md` — update example `throw new RuntimeError(...)` patterns to typed form.
- `.github/instructions/static-analysis.instructions.md` — register `SA0860` in the diagnostic descriptors section.
- `CHANGELOG.md` — under Breaking Changes (C# API only — no Stash-language behavior change).

## 14. Risks & Mitigations

| Risk                                                                                                | Mitigation                                                                                                                                                       |
| --------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Massive PR sizes for Stdlib (629 sites) and VM (175) make review impractical.                       | Phase B can be split per-namespace (one PR per `*BuiltIns.cs`). Phase C can be split VM/Core/Cli.                                                                |
| Source generator changes break existing throws-metadata snapshots.                                  | Phase A leaves the throws generator untouched; Phase D updates it. Snapshot tests get re-baselined as part of Phase D, with diff-only reviewable.                |
| `BuiltInClrType` adds a CLR `Type` reference to every `StashError`. AOT trim warnings?              | `Type` references to known types are AOT-safe (the registry's static dictionary already roots them). Verify with `dotnet publish -c Release -p:PublishAot=true` on Stash.Cli during Phase A. |
| Performance regression from typed-match opcode dispatch.                                            | The two-step (identity then name fallback) check is `O(1)` per catch attempt. Microbenchmarks in Phase C compare before/after for a tight `try/catch` loop.       |
| User code that depends on `RuntimeError` being directly constructible with arbitrary error type breaks. | Search of public API surface confirms no such usage outside the codebase. Document in CHANGELOG. (This is C# API, not Stash language.)                            |
| `SA0860` is too noisy if user codebases legitimately throw built-in names dynamically.              | Rule only fires on string literals. Suppress via `// stashcheck: disable SA0860` if needed.                                                                       |
| `AssertionError` (existing subclass) currently doesn't follow the new convention.                   | Phase A retrofits it with `[StashError(Name = "AssertionError")]` and `override CanonicalName`. Backward-compatible.                                              |

## 15. Open Questions

- **Q1.** Should `RuntimeError` itself become `abstract`, forcing all throws to use a subclass? (Pro: stronger API. Con: loses the "miscellaneous error" escape hatch.) → **Default: keep concrete.** The base class represents a true catch-all; some sites legitimately throw a generic message that doesn't fit any subtype.
- **Q2.** Should `[StashError]` allow subclassing (`class FooError : IOError`)? Use case: a stdlib namespace wants a specialization of `IOError` but `is IOError` should still match. → **Default: no for v1.** `RuntimeError` subclasses must derive directly from `RuntimeError`. Revisit if a real need emerges.
- **Q3.** Do we want a typed-throw syntax for Stash code (`throw IOError("msg")`) in the same spec? → **Decided: no.** Out of scope; sensible follow-up.

## 16. Decision Log

| Date       | Decision                                                                            | Rationale                                                                                                                                                |
| ---------- | ----------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-05-12 | Full migration; old `errorType:` form becomes a compile error.                       | Half-migration leaves the registry-drift problem in place. The whole point of the spec is to *eliminate* the parallel string registries.                  |
| 2026-05-12 | Two parallel hierarchies (`RuntimeError` subclasses + `UserRuntimeError`).           | Built-ins gain type identity; user code retains arbitrary-name flexibility. Cleaner than forcing every error through one channel.                         |
| 2026-05-12 | Source-generator-driven registry, not reflection.                                    | AOT compatibility (Stash.Cli, Check, Format are AOT). Mirrors the proven stdlib-generator pattern.                                                       |
| 2026-05-12 | Top-level classes in `Stash.Runtime.Errors`, one file per type.                      | `using Stash.Runtime.Errors;` plus IDE auto-import is the cleanest call-site experience. Nested form (`StashErrors.IOError`) was rejected as too verbose. |
| 2026-05-12 | **No `CanonicalName` property, no string constants per error class.** The C# class name **is** the Stash name; `BuiltInErrorRegistry.NameOf(ex)` is the single resolver, and it falls through to `GetType().Name`. `[StashError(Name = "...")]` exists only as an explicit, justified escape hatch. `STSE003` blocks reintroduction of any `CanonicalName`/`ErrorType`/`TypeName` member. | The whole point of this refactor is to delete hand-maintained name strings. Adding a per-class `CanonicalName` virtual would just relocate the magic strings from `StashErrorTypes` constants into N subclass overrides — same drift, different file. The class identity is sufficient. |
| 2026-05-12 | Type-identity matching with name fallback for user-collision cases.                  | Identity is fast for the common case (C#-raised errors). Name fallback preserves user-throw semantics — `throw { type: "IOError" }` still matches.        |
| 2026-05-12 | `SA0860` warns on user throws using built-in names; does not error.                  | Some user code legitimately re-throws with a string `type` field; warn-only avoids breaking such code while nudging toward typed forms.                   |
| 2026-05-12 | `[StashFn(Throws = ...)]` attribute changes from `string[]` to `Type[]`.             | Aligns with the new doc-tag form (`<exception cref="IOError">`). String form is the precise drift problem this spec exists to fix.                       |
| 2026-05-12 | 5-phase delivery (A: hierarchy + generator, B: Stdlib migration, C: VM/Core, D: throws-metadata generator, E: deletions + analyzer + docs). | Each phase is a self-contained, reviewable unit. Phase A de-risks the pattern; Phase E only happens once everything else lands.                          |
