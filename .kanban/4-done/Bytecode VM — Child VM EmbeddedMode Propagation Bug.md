# Bytecode VM — Child VM EmbeddedMode Propagation Bug

**Status:** Backlog — Bug Report
**Created:** 2026-04-05
**Severity:** Low (unlikely in practice, potentially catastrophic if triggered)
**Discovery context:** Found during review of "DAP Bytecode VM — Exclusive Debugger Migration"

---

## Bug Description

When `VirtualMachine.LoadModule()` creates a child VM for imported modules, it does **not** propagate `EmbeddedMode`. If an imported module calls `sys.exit()`, the child VM will call `System.Environment.Exit()` instead of throwing an `ExitException`, terminating the entire host process (DAP server, CLI, Playground, etc.).

## Root Cause

In `Stash.Bytecode/VirtualMachine.cs`, the `LoadModule()` method propagates several context properties to child VMs but omits `EmbeddedMode`:

```csharp
// Line ~2340 in VirtualMachine.cs
var moduleVM = new VirtualMachine(moduleGlobals, _ct)
{
    _moduleLoader = _moduleLoader,
    _moduleCache = _moduleCache,
};
moduleVM._context.CurrentFile = resolvedPath;
moduleVM._context.Output = _context.Output;
moduleVM._context.ErrorOutput = _context.ErrorOutput;
moduleVM._context.Input = _context.Input;
moduleVM.Debugger = _debugger;
moduleVM._debugThreadId = _debugThreadId;
// MISSING: moduleVM.EmbeddedMode = EmbeddedMode;
```

## Reproduction Steps

1. Create a module file (`helper.stash`) containing `sys.exit(1);`
2. Create a main script that imports it: `import "helper";`
3. Run through DAP or the Playground (both set `EmbeddedMode = true`)
4. **Expected:** `ExitException` thrown, caught by host
5. **Actual:** `System.Environment.Exit(1)` terminates the process

## Affected Hosts

- **DAP debugger** — `EmbeddedMode = true` set in `DebugSession.Launch()`
- **Playground** — `EmbeddedMode = true` set in `PlaygroundExecutor`
- **StashEngine** — `EmbeddedMode = true` set in `EnsureVM()`

## Fix

Add `moduleVM.EmbeddedMode = EmbeddedMode;` after the other context propagation lines in `LoadModule()`. Also audit other properties that should propagate: `TestHarness`, `TestFilter`, `ScriptArgs`.

## Pre-existing Since

Phase 8 (Bytecode VM — Integration and Migration). The `LoadModule()` method was written in Phase 8 without `EmbeddedMode` propagation.
