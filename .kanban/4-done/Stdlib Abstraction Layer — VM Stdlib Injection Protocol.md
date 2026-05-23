# Stdlib Abstraction Layer — VM Stdlib Injection Protocol

**Status:** Backlog — Design Spec
**Created:** 2026-04-16
**Parent Spec:** [Bytecode VM — Platform Target Readiness](../../.kanban/1-todo/Bytecode%20VM%20—%20Platform%20Target%20Readiness.md) (Section 6.2)
**Purpose:** Define a formal protocol for injecting, composing, and validating standard library namespaces in the Stash bytecode VM — enabling external languages to provide their own stdlib implementations while preserving Stash's zero-allocation performance characteristics.

---

## Table of Contents

1. [Motivation](#1-motivation)
2. [Current State Assessment](#2-current-state-assessment)
3. [Design Goals & Non-Goals](#3-design-goals--non-goals)
4. [Proposed Design](#4-proposed-design)
5. [API Surface](#5-api-surface)
6. [Stdlib Manifest in `.stashc`](#6-stdlib-manifest-in-stashc)
7. [Interaction with Existing Features](#7-interaction-with-existing-features)
8. [Cross-Platform Considerations](#8-cross-platform-considerations)
9. [Implementation Impacts](#9-implementation-impacts)
10. [LSP & DAP Implications](#10-lsp--dap-implications)
11. [Test Scenarios](#11-test-scenarios)
12. [Migration & Breaking Changes](#12-migration--breaking-changes)
13. [Risks & Tradeoffs](#13-risks--tradeoffs)
14. [Decision Log](#14-decision-log)

---

## 1. Motivation

The Platform Target Readiness spec identifies the VM's stdlib injection as "just a globals dictionary" — and that's both its strength and its weakness. The `Dictionary<string, StashValue>` constructor parameter is beautifully simple: any language can throw whatever globals it wants into the dict, and the VM runs. No protocol, no schema, no ceremony.

But this simplicity breaks down in four real scenarios:

### 1.1 External Language Authors Can't Build Namespaces Efficiently

The `NamespaceBuilder` fluent API — which co-registers runtime implementations and tooling metadata in a single call — lives in `Stash.Stdlib`. An external language author who depends only on `Stash.Core` + `Stash.Bytecode` (the correct minimal dependency set) cannot use it. They must manually construct `StashNamespace` objects via `Define()`, create `BuiltInFunction` instances by hand, and have no access to the metadata registration system at all.

### 1.2 No Dependency Validation

If bytecode references global `"fs"` but the VM was instantiated with `StashCapabilities.None` (which excludes the `fs` namespace), the error surfaces as a runtime `RuntimeError` deep in execution. There's no load-time validation that the bytecode's stdlib requirements match what's provided. This is fine when you control both the compiler and the runtime — it's unacceptable when they're built by different teams.

### 1.3 No Stdlib Composition Protocol

Two stdlib providers cannot cleanly merge their contributions. If an external language wants "Stash's `arr` + `str` + `math` namespaces, plus my own `mylib` namespace, minus `fs` and `http`", there's no API for expressing this. The caller must manually cherry-pick entries from `StdlibDefinitions.CreateVMGlobals()` and merge with their own dict — fragile and undocumented.

### 1.4 The Context Interface Is Monolithic

`IInterpreterContext` inherits from five interfaces: `IExecutionContext`, `IProcessContext`, `ITestContext`, `ITemplateContext`, `IFileWatchContext`. A built-in function that just needs `Output` and `CancellationToken` must accept a context that carries test harness hooks, process tracking, template rendering, and file watcher state. External stdlib authors face a daunting interface when all they want is I/O.

---

## 2. Current State Assessment

### 2.1 How Stdlib Injection Works Today

```
StdlibDefinitions._registry                    NamespaceBuilder
  (Func<NamespaceDefinition>, Capability)[]        "arr"
              │                                      │
              ▼                                      ▼
  BuildVMGlobals(capabilities)              .Function("push", ...)
              │                             .Function("pop", ...)
              ▼                             .Build()
  Dictionary<string, StashValue> {                    │
    "arr":    StashValue → StashNamespace,            ▼
    "dict":   StashValue → StashNamespace,    NamespaceDefinition {
    "fs":     StashValue → StashNamespace,      Name, Namespace (runtime),
    "len":    StashValue → BuiltInFunction,     Functions (metadata),
    "typeof": StashValue → BuiltInFunction,     Structs, Enums, Constants
    ...                                       }
  }
              │
              ▼
  new VirtualMachine(globals)
```

### 2.2 Assembly Dependency Graph

```
Stash.Core (no deps)
  ├── StashValue, StashValueTag
  ├── StashNamespace, StashNamespace.Define(), StashNamespace.Freeze()
  ├── BuiltInFunction, BuiltInFunction.DirectHandler
  ├── IInterpreterContext (+ 5 parent interfaces)
  ├── StashCapabilities [Flags] enum
  └── IStashCallable

Stash.Bytecode → Stash.Core
  ├── VirtualMachine(Dictionary<string, StashValue>)
  ├── VMContext : IInterpreterContext (internal)
  ├── ChunkBuilder, Chunk, OpCode, Instruction
  └── StashEngine.CreateFunction(), StashEngine.SetGlobal()

Stash.Stdlib → Stash.Core
  ├── StdlibDefinitions.CreateVMGlobals(StashCapabilities)
  ├── NamespaceBuilder (fluent API)
  ├── NamespaceDefinition, NamespaceFunction, BuiltInParam
  ├── 32 *BuiltIns.cs files (ArrBuiltIns, IoBuiltIns, etc.)
  └── P.Param() helper
```

**Key observation:** The types an external stdlib author needs — `StashNamespace`, `BuiltInFunction`, `StashValue`, `StashCapabilities` — are all in `Stash.Core`. The convenient _builder_ API (`NamespaceBuilder`) is in `Stash.Stdlib`. The _engine-level_ convenience API (`StashEngine.CreateFunction`) is in `Stash.Bytecode` but uses the legacy boxed path (`List<object?>` → boxing/unboxing), not the zero-alloc `DirectHandler` path.

### 2.3 What External Authors Must Do Today

To provide a custom stdlib namespace with good performance:

```csharp
// Step 1: Create namespace manually (Stash.Core types only)
var myNs = new StashNamespace("mylib") { IsBuiltIn = true };

// Step 2: Create BuiltInFunction with DirectHandler (zero-alloc path)
myNs.Define("greet", new BuiltInFunction("mylib.greet", 1,
    static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
    {
        string name = (string)args[0].AsObj!;
        ctx.Output.WriteLine($"Hello, {name}!");
        return StashValue.Null;
    }));

// Step 3: Freeze for IC eligibility
myNs.Freeze();

// Step 4: Merge into globals dict
var globals = StdlibDefinitions.CreateVMGlobals(StashCapabilities.None);
globals["mylib"] = StashValue.FromObj(myNs);

// Step 5: Create VM
var vm = new VirtualMachine(globals);
```

This works but has problems:

- No parameter metadata (names, types, docs) — tooling is blind
- Must reference `Stash.Stdlib` to get Stash's own namespaces
- Must manually call `Freeze()` — forgetting breaks IC
- No validation that the namespace name doesn't collide
- No way to declare capability requirements for custom namespaces

---

## 3. Design Goals & Non-Goals

### Goals

1. **A public, assembly-minimal namespace construction API** that external languages can use without depending on `Stash.Stdlib`. Must support the zero-alloc `DirectHandler` path.

2. **A composable provider protocol** that allows mixing Stash's built-in namespaces with custom ones, with explicit control over which namespaces are included.

3. **A stdlib dependency manifest** embeddable in `.stashc` files, enabling load-time validation of required globals before execution begins.

4. **A minimal context interface** for external stdlib authors who don't need test harness hooks, template rendering, or file watcher tracking.

5. **Backward compatibility.** The existing `Dictionary<string, StashValue>` injection path continues to work unchanged. The `VirtualMachine` constructor signature does not change.

### Non-Goals

- **Replacing Stash.Stdlib.** The existing `StdlibDefinitions` / `NamespaceBuilder` / `*BuiltIns.cs` system continues to be the source of truth for Stash's own standard library. This spec adds an abstraction _below_ it, not a replacement.
- **Type system abstraction.** We don't define how an external language maps its type concepts onto `StashValue`. That remains the external compiler's responsibility (per Platform Target Readiness §6.1).
- **Module system abstraction.** Module loading (`Import` opcode) remains Stash-specific. External languages provide their own `ModuleLoader` callback (per Platform Target Readiness §4.7).
- **Dynamic namespace extension.** We don't support adding members to frozen namespaces at runtime. Once frozen, a namespace is immutable — this is required for IC correctness.

---

## 4. Proposed Design

### 4.1 Architecture Overview

```
External Language                  Stash's Own Stdlib
     │                                    │
     ▼                                    ▼
IStdlibProvider                    IStdlibProvider
  (custom impl)                  (StashStdlibProvider)
     │                                    │
     └──────────┐          ┌──────────────┘
                ▼          ▼
           StdlibComposer
             .Add(provider1)
             .Add(provider2)
             .AddGlobal("myFunc", fn)
             .Exclude("fs", "http")
                    │
                    ▼
        Dictionary<string, StashValue>
                    │
                    ▼
           VirtualMachine(globals)
```

Three new public types, all in `Stash.Core`:

| Type              | Role                                                                    |
| ----------------- | ----------------------------------------------------------------------- |
| `IStdlibProvider` | Interface for anything that can contribute namespaces/globals to the VM |
| `StdlibComposer`  | Composes multiple providers, applies filters, builds final globals dict |
| `IBuiltInContext` | Minimal context interface for external stdlib implementations           |

One new type in `Stash.Stdlib`:

| Type                  | Role                                                 |
| --------------------- | ---------------------------------------------------- |
| `StashStdlibProvider` | `IStdlibProvider` wrapper around `StdlibDefinitions` |

One new section in the `.stashc` binary format:

| Section         | Role                                                   |
| --------------- | ------------------------------------------------------ |
| Stdlib Manifest | Declares which globals/namespaces the bytecode expects |

### 4.2 Design Principles

1. **The dict is still king.** `IStdlibProvider` and `StdlibComposer` are convenience layers that produce a `Dictionary<string, StashValue>`. They don't change the VM's interface. Anyone who wants to skip the abstraction and build a raw dict still can.

2. **Zero-alloc path is the default.** The namespace construction API uses `BuiltInFunction.DirectHandler` (`ReadOnlySpan<StashValue>`) as the primary function registration signature. The boxed `List<object?>` path is available but clearly marked as the legacy/convenience overload.

3. **Metadata is optional but encouraged.** An external stdlib can register functions with _just_ a name, arity, and handler. Parameter names, types, and documentation are optional. If provided, they flow through to LSP/analysis.

4. **Freeze is automatic.** The construction API calls `Freeze()` on `Build()`. External authors cannot forget.

---

## 5. API Surface

### 5.1 `IBuiltInContext` — Minimal Context Interface

**Location:** `Stash.Core/Runtime/IBuiltInContext.cs`

```csharp
/// Minimal context for built-in function implementations.
/// External stdlib authors should program against this interface
/// rather than the full IInterpreterContext.
public interface IBuiltInContext
{
    TextWriter Output { get; }
    TextWriter ErrorOutput { get; }
    TextReader Input { get; }
    CancellationToken CancellationToken { get; }

    /// Invoke a user-provided callback (e.g. a comparator, predicate, or mapper).
    StashValue InvokeCallbackDirect(IStashCallable callable, ReadOnlySpan<StashValue> args);
}
```

`IExecutionContext` extends `IBuiltInContext` (adding `LastError`, `CurrentFile`, `Debugger`, etc.). This is a non-breaking change — it just factors out the base members that were already on `IExecutionContext`.

**Rationale:** A stdlib function that sorts an array needs I/O (to report errors), cancellation (to abort long operations), and callback invocation (to call the user's comparator). It does _not_ need test hooks, process tracking, template rendering, or file watcher state. `IBuiltInContext` is the minimal contract.

**Impact on `BuiltInFunction.DirectHandler`:** The delegate signature does _not_ change. It still receives `IInterpreterContext`. But external stdlib authors can downcast or write helper methods that accept `IBuiltInContext`, and their code will work with any context implementation that satisfies the minimal interface. The full `IInterpreterContext` is always available for Stash's own stdlib that needs the richer API.

> **Decision:** We do NOT change the `DirectHandler` delegate signature to `IBuiltInContext`. Doing so would break all 32 existing `*BuiltIns.cs` files. Instead, `IBuiltInContext` is an advisory interface — external authors _should_ depend only on its members, but they _receive_ the full `IInterpreterContext` at runtime.
>
> **Alternative considered:** A second delegate type `ExternalHandler(IBuiltInContext, ReadOnlySpan<StashValue>)` that the framework wraps. Rejected — introduces a bridge allocation and complicates the call path for no real benefit.
>
> **Alternative considered:** Changing the delegate to `IBuiltInContext`. Rejected — 200+ callsites break, and Stash's own stdlib legitimately uses `IInterpreterContext` members (e.g., `ctx.InvokeCallbackDirect`, `ctx.CurrentFile`, `ctx.TestHarness`).

### 5.2 `IStdlibProvider` — Provider Interface

**Location:** `Stash.Core/Runtime/Stdlib/IStdlibProvider.cs`

```csharp
/// A source of globals (namespaces and/or standalone functions)
/// that can be injected into the bytecode VM.
public interface IStdlibProvider
{
    /// Returns the namespaces this provider contributes.
    /// Each entry contains a runtime StashNamespace (already frozen)
    /// and optional metadata for tooling.
    IReadOnlyList<StdlibNamespaceEntry> GetNamespaces(StashCapabilities capabilities);

    /// Returns standalone global functions/values this provider contributes
    /// (e.g., "len", "typeof", "nameof" — not inside a namespace).
    IReadOnlyList<StdlibGlobalEntry> GetGlobals(StashCapabilities capabilities);
}
```

### 5.3 `StdlibNamespaceEntry` and `StdlibGlobalEntry` — Provider Output Types

**Location:** `Stash.Core/Runtime/Stdlib/StdlibEntry.cs`

```csharp
/// A namespace contributed by an IStdlibProvider.
public sealed record StdlibNamespaceEntry(
    /// Name the namespace is registered under in the globals dict (e.g., "arr", "fs").
    string Name,

    /// The runtime namespace object (must be frozen).
    StashNamespace Namespace,

    /// Capabilities required for this namespace to be available.
    StashCapabilities RequiredCapability = StashCapabilities.None,

    /// Optional function metadata for tooling (LSP completion, hover, etc.).
    /// Null means "no metadata available" — the namespace still works at runtime.
    IReadOnlyList<StdlibFunctionMeta>? Functions = null,

    /// Optional constant metadata for tooling.
    IReadOnlyList<StdlibConstantMeta>? Constants = null
);

/// A standalone global (function or value) contributed by an IStdlibProvider.
public sealed record StdlibGlobalEntry(
    /// Name the global is registered under (e.g., "len", "typeof").
    string Name,

    /// The runtime value.
    StashValue Value,

    /// Capabilities required for this global.
    StashCapabilities RequiredCapability = StashCapabilities.None,

    /// Optional metadata for tooling. Null if this is not a function.
    StdlibFunctionMeta? FunctionMeta = null
);

/// Tooling metadata for a single function in a namespace.
public sealed record StdlibFunctionMeta(
    string Name,
    StdlibParamMeta[] Parameters,
    string? ReturnType = null,
    bool IsVariadic = false,
    string? Documentation = null
);

/// Tooling metadata for a function parameter.
public sealed record StdlibParamMeta(
    string Name,
    string? Type = null
);

/// Tooling metadata for a namespace constant.
public sealed record StdlibConstantMeta(
    string Name,
    string Type,
    string DisplayValue,
    string? Documentation = null
);
```

### 5.4 `StdlibComposer` — Composition Engine

**Location:** `Stash.Core/Runtime/Stdlib/StdlibComposer.cs`

```csharp
/// Composes multiple IStdlibProviders into a single globals dictionary
/// suitable for VirtualMachine construction.
public sealed class StdlibComposer
{
    private readonly List<IStdlibProvider> _providers = [];
    private readonly Dictionary<string, StashValue> _extraGlobals = [];
    private readonly HashSet<string> _excludedNames = [];
    private StashCapabilities _capabilities = StashCapabilities.All;

    /// Add a provider. Providers are evaluated in registration order.
    /// Later providers override earlier ones on name collision.
    public StdlibComposer Add(IStdlibProvider provider);

    /// Add a single global function or value directly.
    public StdlibComposer AddGlobal(string name, StashValue value);

    /// Add a single global built-in function with the zero-alloc handler.
    public StdlibComposer AddGlobal(string name, int arity,
        BuiltInFunction.DirectHandler handler);

    /// Exclude specific namespace/global names from all providers.
    public StdlibComposer Exclude(params string[] names);

    /// Set the capabilities mask. Namespaces whose RequiredCapability
    /// is not satisfied are silently omitted.
    public StdlibComposer WithCapabilities(StashCapabilities capabilities);

    /// Build the final globals dictionary.
    /// Namespaces are frozen (if not already).
    /// Name collisions from later providers override earlier ones.
    /// Excluded names are removed after all providers contribute.
    public Dictionary<string, StashValue> Build();

    /// Build and return both the globals dictionary and a StdlibManifest
    /// listing all contributed names (for embedding in .stashc).
    public (Dictionary<string, StashValue> Globals, StdlibManifest Manifest) BuildWithManifest();
}
```

**Collision semantics:** Last-writer-wins by provider registration order. This is the same semantics as merging multiple dictionaries — simple, predictable, no surprises.

> **Decision:** Last-writer-wins on collision, not throw-on-collision.
>
> **Rationale:** Throwing on collision makes composition fragile — you can't override a single function in a namespace without removing the entire namespace first. Last-writer-wins lets you layer: "start with Stash's stdlib, then overlay my custom `io` namespace that replaces `io.println` behavior."
>
> **Risk:** Silent overrides could mask bugs. Mitigated by the `StdlibComposer` logging overrides to debug output if a diagnostic writer is configured.

### 5.5 `StashStdlibProvider` — Stash's Own Stdlib as Provider

**Location:** `Stash.Stdlib/StashStdlibProvider.cs`

```csharp
/// IStdlibProvider that wraps Stash's built-in standard library.
/// This is the bridge between the existing StdlibDefinitions registry
/// and the new provider protocol.
public sealed class StashStdlibProvider : IStdlibProvider
{
    public IReadOnlyList<StdlibNamespaceEntry> GetNamespaces(StashCapabilities capabilities)
    {
        // Delegates to StdlibDefinitions.GetNamespaces(capabilities)
        // and adapts NamespaceDefinition → StdlibNamespaceEntry
    }

    public IReadOnlyList<StdlibGlobalEntry> GetGlobals(StashCapabilities capabilities)
    {
        // Delegates to StdlibDefinitions.GetGlobalNamespace(capabilities)
        // and adapts to StdlibGlobalEntry
    }
}
```

This is a thin adapter. `StdlibDefinitions` remains the internal implementation. `StashStdlibProvider` is the public face.

### 5.6 `StdlibNamespaceBuilder` — Namespace Construction API

**Location:** `Stash.Core/Runtime/Stdlib/StdlibNamespaceBuilder.cs`

This is a public namespace construction API that lives in `Stash.Core`, not `Stash.Stdlib`. It's a simplified version of the existing `NamespaceBuilder` that produces `StdlibNamespaceEntry` directly.

```csharp
/// Fluent builder for constructing a stdlib namespace with both
/// runtime implementation and optional tooling metadata.
public sealed class StdlibNamespaceBuilder
{
    private readonly string _name;
    private readonly StashNamespace _namespace;
    private readonly List<StdlibFunctionMeta> _functions = [];
    private readonly List<StdlibConstantMeta> _constants = [];
    private StashCapabilities _requiredCapability = StashCapabilities.None;

    public StdlibNamespaceBuilder(string name)
    {
        _name = name;
        _namespace = new StashNamespace(name) { IsBuiltIn = true };
    }

    /// Register a function with the zero-alloc DirectHandler signature.
    /// Parameter metadata is optional but enables LSP completion/hover.
    public StdlibNamespaceBuilder Function(
        string name,
        int arity,
        BuiltInFunction.DirectHandler handler,
        StdlibParamMeta[]? parameters = null,
        string? returnType = null,
        bool isVariadic = false,
        string? documentation = null)
    {
        int effectiveArity = isVariadic ? -1 : arity;
        string qualifiedName = $"{_name}.{name}";
        _namespace.Define(name, new BuiltInFunction(qualifiedName, effectiveArity, handler));

        if (parameters is not null)
        {
            _functions.Add(new StdlibFunctionMeta(
                name, parameters, returnType, isVariadic, documentation));
        }
        return this;
    }

    /// Register a constant value.
    public StdlibNamespaceBuilder Constant(
        string name,
        StashValue value,
        string type,
        string displayValue,
        string? documentation = null)
    {
        _namespace.Define(name, value.ToObject());
        _constants.Add(new StdlibConstantMeta(name, type, displayValue, documentation));
        return this;
    }

    /// Declare the capability requirement for this namespace.
    public StdlibNamespaceBuilder RequiresCapability(StashCapabilities capability)
    {
        _requiredCapability = capability;
        return this;
    }

    /// Build the namespace entry. Freezes the namespace automatically.
    public StdlibNamespaceEntry Build()
    {
        _namespace.Freeze();
        return new StdlibNamespaceEntry(
            _name,
            _namespace,
            _requiredCapability,
            _functions.Count > 0 ? _functions : null,
            _constants.Count > 0 ? _constants : null);
    }
}
```

**Key differences from `Stash.Stdlib.NamespaceBuilder`:**

| Aspect                   | `Stash.Stdlib.NamespaceBuilder`     | `StdlibNamespaceBuilder` (new)          |
| ------------------------ | ----------------------------------- | --------------------------------------- |
| Assembly                 | `Stash.Stdlib`                      | `Stash.Core`                            |
| Output type              | `NamespaceDefinition`               | `StdlibNamespaceEntry`                  |
| Metadata format          | `NamespaceFunction`, `BuiltInParam` | `StdlibFunctionMeta`, `StdlibParamMeta` |
| Struct/Enum registration | Yes                                 | No (structs/enums are Stash-specific)   |
| Parameter specification  | `BuiltInParam[]` required           | `StdlibParamMeta[]?` optional           |
| Freeze                   | Manual (caller must freeze)         | Automatic on `Build()`                  |

> **Decision:** `StdlibNamespaceBuilder` does NOT support struct or enum registration.
>
> **Rationale:** Structs and enums are Stash language constructs. An external language targeting the Stash VM would define its own type concepts that compile to struct/enum opcodes — but it wouldn't register them through the stdlib builder. The struct/enum metadata types are becoming public via Platform Target Readiness §5.1, so external compilers can emit `StructDecl` / `EnumDecl` opcodes directly.
>
> **Alternative considered:** Include struct/enum registration for languages that want to reuse Stash's type system. Deferred — let demand drive this.

---

## 6. Stdlib Manifest in `.stashc`

### 6.1 Purpose

A stdlib manifest is an optional section in a `.stashc` file that declares which globals the bytecode expects to find at runtime. This enables:

1. **Load-time validation:** The VM checks that all required globals are present before executing any instruction. Errors like "namespace 'fs' not found" surface immediately with a clear message, not as a runtime crash 500 instructions deep.

2. **Dependency documentation:** External tools can inspect a `.stashc` file and determine its stdlib requirements without executing it.

3. **Lazy loading hint:** A future VM optimization could defer namespace construction until first access, using the manifest to validate capability requirements upfront.

### 6.2 Manifest Structure

```csharp
/// Declares the stdlib globals that a compiled chunk expects.
public sealed record StdlibManifest(
    /// Namespace names the bytecode references (e.g., ["arr", "io", "fs"]).
    IReadOnlyList<string> RequiredNamespaces,

    /// Standalone global names the bytecode references (e.g., ["len", "typeof"]).
    IReadOnlyList<string> RequiredGlobals,

    /// Minimum capability set needed to satisfy all requirements.
    StashCapabilities MinimumCapabilities
);
```

### 6.3 Manifest Generation

The manifest is built by the compiler from the set of globals actually referenced in the bytecode. The Stash compiler can extract this from the `GlobalSlotAllocator`'s name table — every `GetGlobal` opcode references a named slot, and the set of referenced names is the manifest.

An external compiler builds the manifest from its own global reference tracking and embeds it via `ChunkBuilder`:

```csharp
// Proposed API addition to ChunkBuilder
public ChunkBuilder SetStdlibManifest(StdlibManifest manifest);
```

### 6.4 Binary Format Extension

The manifest is encoded as a new optional section in the `.stashc` format, after the existing sections:

```
Section Tag:    0x05 (StdlibManifest)
Version:        uint8 (1)
Namespace Count: uint16
  For each namespace:
    Name Length: uint16
    Name Bytes:  UTF-8
Global Count:   uint16
  For each global:
    Name Length: uint16
    Name Bytes:  UTF-8
Capabilities:   uint32 (StashCapabilities flags)
```

Optional sections are forward-compatible — a reader that doesn't understand section tag `0x05` skips it using the section length prefix already defined in the `.stashc` format.

### 6.5 VM Validation Behavior

```csharp
// Proposed behavior in VirtualMachine.Execute(Chunk)
if (chunk.StdlibManifest is { } manifest)
{
    foreach (string ns in manifest.RequiredNamespaces)
    {
        if (!_globals.ContainsKey(ns))
            throw new RuntimeError(
                $"Bytecode requires namespace '{ns}' but it is not available. " +
                $"Ensure the VM is configured with the required stdlib provider.");
    }
    foreach (string global in manifest.RequiredGlobals)
    {
        if (!_globals.ContainsKey(global))
            throw new RuntimeError(
                $"Bytecode requires global '{global}' but it is not available.");
    }
}
```

> **Decision:** Validation is a soft check — missing globals produce a clear error, but the manifest is optional. Bytecode without a manifest executes with no validation (backward compatible).
>
> **Alternative considered:** Hard requirement — all `.stashc` files must have a manifest. Rejected — breaks all existing `.stashc` files and the non-serialized `Chunk` path (REPL, inline evaluation).
>
> **Alternative considered:** Warnings instead of errors. Rejected — if the bytecode says it needs `fs` and `fs` isn't there, execution _will_ fail. Better to fail fast with a clear message than to fail cryptically later.

---

## 7. Interaction with Existing Features

### 7.1 Inline Caching

The IC system guards on namespace _identity_ (`objVal.AsObj == ic.Guard`). This means:

- Two `StdlibNamespaceEntry` instances with the same name but different `StashNamespace` objects are distinct to the IC. No false cache hits.
- A namespace must be frozen for IC to cache its members. `StdlibNamespaceBuilder.Build()` freezes automatically.
- Replacing a namespace (via `StdlibComposer` override) produces a new `StashNamespace` identity, so IC slots populated with the old namespace will miss and re-cache correctly.

**No IC changes required.**

### 7.2 StashEngine API

`StashEngine` currently has `CreateFunction()` and `SetGlobal()`. These should be updated to also accept `IStdlibProvider`:

```csharp
// Proposed addition to StashEngine
public StashEngine AddStdlibProvider(IStdlibProvider provider);
```

Internally, `StashEngine` would use `StdlibComposer` to merge its default stdlib with any additional providers before creating the VM.

### 7.3 Scope Rules and Name Resolution

Global names injected via the stdlib are resolved by the `GetGlobal` opcode, which indexes into the VM's `_globals` dictionary by slot. The `GlobalSlotAllocator` assigns slots at compile time. An external compiler must assign global slots consistent with the names it expects to find in the globals dictionary.

**No change to scope resolution.** The stdlib provider just determines _what's in the dict_ — the VM's lookup mechanism is unchanged.

### 7.4 Error Handling

Built-in functions throw exceptions that the VM catches and wraps. This behavior is provider-agnostic — the VM doesn't know or care whether a `BuiltInFunction` came from Stash's stdlib or an external provider. The exception handling path is:

```
BuiltInFunction throws Exception
  → VM catches (unless RuntimeError/OperationCanceledException/TemplateException)
  → Wraps in RuntimeError("Built-in function error: {message}", span)
  → Propagates to try-catch handler or top-level
```

**No error handling changes required.**

### 7.5 Capabilities System

`StashCapabilities` is a `[Flags]` enum with 4 bits (FileSystem, Network, Process, Environment). External providers declare requirements using the same flags.

> **Decision:** Do NOT extend `StashCapabilities` with custom flags for external providers.
>
> **Rationale:** Custom capability flags would require a `ulong` or string-based system, breaking the simple bitfield. External providers that need custom capability concepts should implement their own filtering logic inside `GetNamespaces()` — the `StashCapabilities` parameter tells them the _VM's_ security posture, and they decide what to expose based on that plus their own criteria.
>
> **Alternative considered:** A `Dictionary<string, bool>` capabilities map for extensibility. Rejected — stringly-typed, slower, and the 4 existing capabilities cover the meaningful security boundaries (I/O, network, process, environment). An external language that needs finer-grained control should implement it in their provider, not in the shared protocol.

### 7.6 REPL and Interactive Evaluation

The REPL creates a `VirtualMachine` with Stash's full stdlib. With the new protocol, the REPL would use:

```csharp
var composer = new StdlibComposer()
    .Add(new StashStdlibProvider())
    .WithCapabilities(StashCapabilities.All);
var globals = composer.Build();
var vm = new VirtualMachine(globals);
```

Functionally identical to today's `StdlibDefinitions.CreateVMGlobals(StashCapabilities.All)`. The composer just makes the intent explicit.

### 7.7 Playground / Sandbox

The Blazor WASM playground currently uses `StashCapabilities.None` to disable filesystem, network, and process access. With the new protocol:

```csharp
var composer = new StdlibComposer()
    .Add(new StashStdlibProvider())
    .Exclude("process", "env")
    .WithCapabilities(StashCapabilities.None);
```

Same result, but `Exclude()` makes the intent explicit beyond just the capability mask.

---

## 8. Cross-Platform Considerations

### 8.1 Platform-Specific Namespaces

Some stdlib functions behave differently across platforms (e.g., `path.sep` returns `"/"` on Linux/macOS and `"\\"` on Windows). This is already handled per-function in the existing `*BuiltIns.cs` files.

External providers face the same challenge. The protocol doesn't impose platform-specific requirements — providers are responsible for their own cross-platform behavior.

### 8.2 Assembly Loading

`Stash.Core` is the dependency for the new types. It's already a required dependency for any Stash VM consumer. No new assembly references required.

### 8.3 Native AOT Compatibility

All new types are plain records, interfaces, and sealed classes. No reflection, no `Type.GetType()`, no dynamic assembly loading. Fully AOT-compatible.

---

## 9. Implementation Impacts

### 9.1 New Types in `Stash.Core`

| Type                     | Location                                              |
| ------------------------ | ----------------------------------------------------- |
| `IBuiltInContext`        | `Stash.Core/Runtime/IBuiltInContext.cs`               |
| `IStdlibProvider`        | `Stash.Core/Runtime/Stdlib/IStdlibProvider.cs`        |
| `StdlibNamespaceEntry`   | `Stash.Core/Runtime/Stdlib/StdlibEntry.cs`            |
| `StdlibGlobalEntry`      | `Stash.Core/Runtime/Stdlib/StdlibEntry.cs`            |
| `StdlibFunctionMeta`     | `Stash.Core/Runtime/Stdlib/StdlibEntry.cs`            |
| `StdlibParamMeta`        | `Stash.Core/Runtime/Stdlib/StdlibEntry.cs`            |
| `StdlibConstantMeta`     | `Stash.Core/Runtime/Stdlib/StdlibEntry.cs`            |
| `StdlibComposer`         | `Stash.Core/Runtime/Stdlib/StdlibComposer.cs`         |
| `StdlibNamespaceBuilder` | `Stash.Core/Runtime/Stdlib/StdlibNamespaceBuilder.cs` |
| `StdlibManifest`         | `Stash.Core/Runtime/Stdlib/StdlibManifest.cs`         |

### 9.2 New Types in `Stash.Stdlib`

| Type                  | Location                              |
| --------------------- | ------------------------------------- |
| `StashStdlibProvider` | `Stash.Stdlib/StashStdlibProvider.cs` |

### 9.3 Modified Types

| Type                | Change                                                      |
| ------------------- | ----------------------------------------------------------- |
| `IExecutionContext` | Now extends `IBuiltInContext`                               |
| `Chunk`             | New optional `StdlibManifest? StdlibManifest` property      |
| `ChunkBuilder`      | New `SetStdlibManifest(StdlibManifest)` method              |
| `BytecodeWriter`    | Serialize manifest section if present                       |
| `BytecodeReader`    | Deserialize manifest section if present                     |
| `VirtualMachine`    | Validate manifest against globals in `Execute()` if present |
| `StashEngine`       | New `AddStdlibProvider(IStdlibProvider)` method             |

### 9.4 No AST Changes

This is purely a runtime/infrastructure feature. No new syntax, no new AST nodes, no parser changes.

### 9.5 Static Analysis Impact

The static analysis engine (`Stash.Analysis`) resolves built-in namespaces and functions for diagnostics. It currently reads from `StdlibDefinitions` directly.

With the new protocol, analysis can optionally accept an `IStdlibProvider` to resolve custom namespaces. However, for Stash's own analysis pipeline, continuing to use `StdlibDefinitions` directly is fine — the provider protocol is primarily for the VM, not the analyzer.

> **Decision:** Defer analysis integration. The analyzer continues to use `StdlibDefinitions`. When a real external language needs analysis support, extend the analysis engine then.

---

## 10. LSP & DAP Implications

### 10.1 LSP Completion & Hover

The LSP server uses `StdlibDefinitions.GetNamespaces()` and its metadata models (`NamespaceFunction`, `BuiltInParam`) for:

- Completion items when typing `arr.` or `io.`
- Hover information showing function signatures and docs
- Signature help for parameter names and types

The new `StdlibFunctionMeta` / `StdlibParamMeta` records are structurally equivalent to the existing metadata models. If the LSP server ever needs to support custom stdlib providers (e.g., for an external language's LSP), the metadata flows through `StdlibNamespaceEntry.Functions`.

**No LSP changes required for Stash's own LSP.** An external language would build its own LSP server.

### 10.2 DAP Debugging

The debugger inspects runtime values via `StashValue`. Since all stdlib values (namespaces, functions) are `StashValue` instances regardless of their provider, the debugger works unchanged.

**No DAP changes required.**

---

## 11. Test Scenarios

### 11.1 `StdlibComposer` Tests

| Test                                    | Expectation                                      |
| --------------------------------------- | ------------------------------------------------ |
| Empty composer → Build()                | Returns empty dictionary                         |
| Single provider → Build()               | All provider namespaces/globals in dict          |
| Two providers, no overlap → Build()     | Union of both providers' contributions           |
| Two providers, name collision → Build() | Later provider's entry wins                      |
| Exclude("fs") → Build()                 | `fs` not in dict, all others present             |
| WithCapabilities(None) → Build()        | Only `None`-capability namespaces present        |
| AddGlobal("custom", fn) → Build()       | Custom global in dict alongside provider globals |
| AddGlobal + Exclude(same name)          | Excluded (Exclude runs after all additions)      |

### 11.2 `StdlibNamespaceBuilder` Tests

| Test                                 | Expectation                                      |
| ------------------------------------ | ------------------------------------------------ |
| Build empty namespace                | Empty frozen StashNamespace                      |
| Add function, build                  | Function callable via namespace.GetMemberValue() |
| Add function with metadata, build    | StdlibFunctionMeta present in entry              |
| Add function without metadata, build | Functions list is null                           |
| Build freezes namespace              | Namespace.IsFrozen == true                       |
| Add constant, build                  | Constant accessible, metadata present            |

### 11.3 `StdlibManifest` Validation Tests

| Test                                         | Expectation                                        |
| -------------------------------------------- | -------------------------------------------------- |
| Manifest requires "arr", globals has "arr"   | Execution succeeds                                 |
| Manifest requires "fs", globals lacks "fs"   | RuntimeError at Execute() before first instruction |
| No manifest on chunk                         | No validation, execution proceeds                  |
| Manifest serialized to .stashc, deserialized | Round-trip preserves all entries                   |

### 11.4 `StashStdlibProvider` Tests

| Test                      | Expectation                              |
| ------------------------- | ---------------------------------------- |
| GetNamespaces(All)        | All 32 namespaces returned               |
| GetNamespaces(None)       | Only capability-free namespaces returned |
| GetNamespaces(FileSystem) | Includes fs, pkg; excludes http, ssh     |
| GetGlobals(All)           | len, typeof, nameof, etc.                |

### 11.5 Integration Tests

| Test                                          | Expectation                                       |
| --------------------------------------------- | ------------------------------------------------- |
| Custom provider → VM → execute bytecode       | Custom namespace functions callable from bytecode |
| Stash provider + custom → compose → execute   | Both Stash and custom functions work              |
| Override Stash's io.println → execute         | Custom println runs instead of Stash's            |
| IC caching with custom namespace              | IC cache hits work (namespace is frozen)          |
| Manifest validation prevents capability error | Clear error message, not deep runtime crash       |

### 11.6 `IBuiltInContext` Tests

| Test                                         | Expectation                                      |
| -------------------------------------------- | ------------------------------------------------ |
| VMContext implements IBuiltInContext         | Implicit via IExecutionContext → IBuiltInContext |
| External function using only IBuiltInContext | Works correctly with VMContext at runtime        |

---

## 12. Migration & Breaking Changes

### 12.1 Binary Compatibility

All existing code continues to work. The changes are purely additive:

- `IExecutionContext` gains a new parent interface (`IBuiltInContext`). Since `IExecutionContext` already has all the members, existing implementations satisfy `IBuiltInContext` automatically. **Source-compatible and binary-compatible.**

- `Chunk` gains an optional `StdlibManifest?` property (default `null`). Existing chunks work unchanged.

- `VirtualMachine` constructor signature is unchanged. The manifest validation is a new code path that only activates when `chunk.StdlibManifest` is non-null.

### 12.2 `.stashc` Format Compatibility

The new manifest section uses an optional section tag. Existing `.stashc` files (which lack this section) are valid. New `.stashc` files with the manifest section are readable by old readers that skip unknown sections.

**Forward and backward compatible.**

### 12.3 No Deprecations

`StdlibDefinitions.CreateVMGlobals()` continues to work. It's the direct path for anyone who doesn't need composition. The new protocol is additive, not a replacement.

---

## 13. Risks & Tradeoffs

### 13.1 Abstraction Overhead

Adding `IStdlibProvider`, `StdlibComposer`, and `StdlibNamespaceBuilder` introduces new types that users must learn. The existing "just build a dict" approach is simpler for trivial cases.

**Mitigation:** The dict approach still works. The new types are for users who need composition, validation, or metadata. The docs should present the dict as the "quick start" and the provider protocol as the "structured approach."

### 13.2 Metadata Duplication

`StdlibFunctionMeta` / `StdlibParamMeta` are structurally similar to `Stash.Stdlib`'s `NamespaceFunction` / `BuiltInParam`. Two parallel metadata hierarchies.

**Mitigation:** The `Stash.Core` types are intentionally minimal (no `BuiltInStruct`, no `BuiltInEnum`, no `NamespaceConstant` with all its fields). `Stash.Stdlib`'s richer metadata is an internal detail. The `StashStdlibProvider` adapter bridges between them. If the duplication proves burdensome, we can make `Stash.Stdlib`'s types extend the `Stash.Core` ones.

**Alternative considered:** Move all metadata types to `Stash.Core`. Rejected — `Stash.Core` should have minimal surface area. Metadata for Stash's 32 namespaces doesn't belong in the core runtime library.

### 13.3 Manifest Accuracy

The manifest is only as accurate as the compiler that generates it. An external compiler that forgets to include a namespace in the manifest will bypass validation. The manifest is a _declaration of intent_, not a formal proof of correctness.

**Mitigation:** For Stash's own compiler, the manifest can be generated automatically from `GlobalSlotAllocator`'s name table. External compilers are responsible for their own correctness — the manifest is a tool to help, not a guarantee.

### 13.4 Public API Commitment

Every new public type is a compatibility obligation. Changing `StdlibNamespaceEntry`'s fields is a breaking change for external providers.

**Mitigation:** The types are sealed records with simple fields. They're structurally stable because they model concepts that don't change (a namespace has a name and members). Use `[Experimental]` attribute if available in the target framework to signal that the API may evolve.

### 13.5 Freezing Semantics

`StdlibNamespaceBuilder.Build()` freezes the namespace automatically. But what if an external provider wants to build a namespace incrementally across multiple phases? Freeze-on-build prevents this.

**Mitigation:** The provider can use `StashNamespace` directly (without the builder) and call `Freeze()` manually when ready. The builder is a convenience, not a requirement. Document this escape hatch.

---

## 14. Decision Log

| Date       | Decision                                          | Rationale                                                                             |
| ---------- | ------------------------------------------------- | ------------------------------------------------------------------------------------- |
| 2026-04-16 | Created as backlog design spec                    | Flesh out deferred item §6.2 from Platform Target Readiness spec                      |
| 2026-04-16 | Place new types in `Stash.Core`, not new assembly | Avoids assembly proliferation; Core already has StashNamespace, BuiltInFunction, etc. |
| 2026-04-16 | Do NOT change DirectHandler signature             | Would break 200+ callsites in 32 BuiltIns files for no runtime benefit                |
| 2026-04-16 | Last-writer-wins collision in composer            | Simple, predictable; matches dict merge semantics; enables layered overrides          |
| 2026-04-16 | Manifest is optional in `.stashc`                 | Backward compatible; non-serialized Chunk path (REPL) doesn't need it                 |
| 2026-04-16 | Do NOT extend StashCapabilities for custom flags  | 4 existing flags cover security boundaries; custom logic belongs in provider impl     |
| 2026-04-16 | No struct/enum registration in new builder        | Structs/enums are Stash-specific; external compilers emit opcodes directly            |
| 2026-04-16 | Defer analysis engine integration                 | Analyzer uses StdlibDefinitions directly; extend when real demand exists              |
| 2026-04-16 | IBuiltInContext as advisory, not enforced         | Avoids delegate signature change; external authors choose their dependency surface    |
