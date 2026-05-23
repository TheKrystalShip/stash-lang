# Import System — Namespace Re-Export Filtering

## Summary

`ExecuteImportAs` in `VirtualMachine.Modules.cs` skips ALL `StashNamespace` values when building the namespace object for an `import ... as alias` statement. This is overly aggressive — it filters out both built-in namespaces (intended) AND user-defined namespace imports (unintended).

## Bug Description

When module A does `import "B.stash" as helper;` and module C does `import "A.stash" as modA;`, `modA.helper` does not exist because the `StashNamespace` filter removed it.

### Reproduction

```stash
// helper.stash
fn greet() { return "hello"; }

// utils.stash
import "helper.stash" as helper;
fn callHelper() { return helper.greet(); }

// main.stash
import "utils.stash" as utils;
utils.callHelper();     // Works — callHelper is a VMFunction
utils.helper.greet();   // FAILS — helper was filtered out as StashNamespace
```

## Root Cause

[VirtualMachine.Modules.cs](Stash.Bytecode/VM/VirtualMachine.Modules.cs) — `ExecuteImportAs`:

```csharp
foreach (KeyValuePair<string, object?> kvp in moduleEnv)
{
    if (kvp.Value is StashNamespace)
    {
        continue; // skip inherited built-in namespaces
    }
    ns.Define(kvp.Key, kvp.Value);
}
```

The filter intends to skip built-in namespaces like `io`, `fs`, `str`, `math`, etc. that were inherited from the parent VM's globals. But it also skips user-created namespace imports.

## Proposed Fix

Distinguish built-in namespaces from user-created ones. Options:

1. **Check if the name matches a known built-in namespace** — compare against `StdlibDefinitions.Namespaces` or a hardcoded set.
2. **Mark StashNamespace with an `IsBuiltIn` flag** — set during stdlib registration, not set for user-created imports.
3. **Compare against the parent globals** — skip only namespaces that were in the original parent globals copy (the ones inherited, not created by the module).

Option 2 is cleanest — add a `bool IsBuiltIn` property to `StashNamespace`, set it true during stdlib registration. The filter then becomes `if (kvp.Value is StashNamespace ns && ns.IsBuiltIn) continue;`.

## Discovery Context

Found during comprehensive import system review (April 2026). Not triggered by any user-reported bug.

## Impact

High — Stash is intended to be modularized, to allow users to import external packages into their projects. This breaks the ability to ship packages that have any internal imports since they can't be seen, accessed or re-exported from the package's "index.stash". Review the [docs/PKG — Package Manager CLI.md](../../docs/PKG%20—%20Package%20Manager%20CLI.md) for more information on package importing and resolution.

## Affected Files

- `Stash.Bytecode/VM/VirtualMachine.Modules.cs` — `ExecuteImportAs()`
- `Stash.Core/Runtime/Types/StashNamespace.cs` — add `IsBuiltIn` property (if option 2)
- `Stash.Stdlib/StdlibDefinitions.cs` — set `IsBuiltIn = true` during registration
