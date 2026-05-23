## Bug — Unsafe `callSpan!.Value` Access in VM Debug Hooks

### Description

In `VirtualMachine.Async.cs` and `VirtualMachine.Functions.cs`, the debug hook code uses `callSpan!.Value` without a null guard. If `GetCurrentSpan()` returns `null`, this throws `InvalidOperationException` at runtime.

### Root Cause

The `callSpan` variable is `SourceSpan?` (returned by `GetCurrentSpan(ref frame)`). The `!` operator suppresses the nullable warning but doesn't prevent a runtime crash when the value is actually null.

This pattern was pre-existing even before the `SourceSpan` struct change — the original code used `callSpan!` with the reference type, which would have passed `null` through to `OnFunctionEnter`, potentially causing an NPE downstream.

### Affected Files

1. **`Stash.Bytecode/VM/VirtualMachine.Async.cs`** — `ExecuteCallSpread` method, line ~179:
   ```csharp
   debugger.OnFunctionEnter(funcName, callSpan!.Value, scope, _debugThreadId);
   ```

2. **`Stash.Bytecode/VM/VirtualMachine.Functions.cs`** — `ExecuteCall` method, line ~431:
   ```csharp
   debugger.OnFunctionEnter(funcName, callSpan!.Value, scope, _debugThreadId);
   ```

### Reproduction

Attach a debugger, trigger a function call where `GetCurrentSpan` returns null (e.g., a call from generated/synthetic code without source mapping). If `ShouldBreakOnFunctionEntry` returns true, the crash occurs.

### Suggested Fix

Add a null guard before accessing `.Value`:
```csharp
if (callSpan is not null && debugger.ShouldBreakOnFunctionEntry(funcName))
{
    debugger.OnFunctionEnter(funcName, callSpan.Value, scope, _debugThreadId);
}
```

### Discovery Context

Found during review of "Performance Analysis: Lexer & Parser" spec (SourceSpan → readonly record struct conversion).
