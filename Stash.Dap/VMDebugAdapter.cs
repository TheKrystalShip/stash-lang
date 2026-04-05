namespace Stash.Dap;

using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Interpreting;
using DebugCallFrame = Stash.Debugging.CallFrame;

/// <summary>
/// Adapts a <see cref="VirtualMachine"/> to the <see cref="IDebugExecutor"/> interface
/// expected by <see cref="DebugSession"/>. Uses a tree-walk <see cref="Interpreter"/>
/// for watch expression evaluation (Phase 7 fallback — full VM eval deferred to Phase 8+).
/// </summary>
internal sealed class VMDebugAdapter : Stash.Debugging.IDebugExecutor
{
    private readonly VirtualMachine _vm;
    private readonly Interpreter _evalInterpreter;

    public VMDebugAdapter(VirtualMachine vm, Environment globals)
    {
        _vm = vm;
        _evalInterpreter = new Interpreter();
        // The eval interpreter shares the same global bindings for consistent results.
        // Copy global bindings that the VM has into the interpreter's global environment.
        foreach (var kvp in vm.Globals)
            _evalInterpreter.Globals.Define(kvp.Key, kvp.Value);
    }

    public IReadOnlyList<DebugCallFrame> CallStack => _vm.DebugCallStack;

    public Stash.Debugging.IDebugScope GlobalScope
    {
        get
        {
            // Build a snapshot of VM globals as an IDebugScope
            var bindings = new KeyValuePair<string, object?>[_vm.Globals.Count];
            int idx = 0;
            foreach (var kvp in _vm.Globals)
                bindings[idx++] = kvp;
            return new VMDebugScope(bindings, null);
        }
    }

    public (object? Value, string? Error) EvaluateExpression(string expression, Stash.Debugging.IDebugScope scope)
    {
        // Build a tree-walk Environment from the IDebugScope for the eval interpreter.
        // The scope's bindings become locals in a temporary environment layered over globals.
        var env = new Environment(_evalInterpreter.Globals, 16);
        foreach (var (name, value) in scope.GetAllBindings())
            env.Define(name, value);

        return _evalInterpreter.EvaluateString(expression, env);
    }
}
