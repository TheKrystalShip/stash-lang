namespace Stash.Dap;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Stash.Bytecode;
using Stash.Debugging;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Runtime;
using DebugCallFrame = Stash.Debugging.CallFrame;

/// <summary>
/// Adapts a <see cref="VirtualMachine"/> to the <see cref="IDebugExecutor"/> interface
/// expected by <see cref="DebugSession"/>. Uses VM-native bytecode compilation and
/// execution for watch expression evaluation — no tree-walk interpreter dependency.
/// </summary>
internal sealed class VMDebugAdapter : IDebugExecutor
{
    private readonly VirtualMachine _vm;

    public VMDebugAdapter(VirtualMachine vm)
    {
        _vm = vm;
    }

    public IReadOnlyList<DebugCallFrame> CallStack => _vm.DebugCallStack;

    public IDebugScope GlobalScope => _vm.BuildGlobalScope();

    public (object? Value, string? Error) EvaluateExpression(string expression, IDebugScope scope)
    {
        try
        {
            // 1. Lex
            var lexer = new Lexer(expression);
            var tokens = lexer.ScanTokens();
            if (lexer.Errors.Count > 0)
                return (null, lexer.Errors[0]);

            // 2. Parse as expression
            var parser = new Parser(tokens);
            var expr = parser.Parse();
            if (parser.Errors.Count > 0)
                return (null, parser.Errors[0]);

            // 3. Compile (no resolver — unresolved variables become OP_LOAD_GLOBAL)
            Chunk chunk = Compiler.CompileExpression(expr);

            // 4. Seed temp VM with scope bindings (outermost first so innermost shadows)
            var tempGlobals = new Dictionary<string, StashValue>();
            var chain = scope.GetScopeChain().ToList();
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var (name, value) in chain[i].GetAllBindings())
                    tempGlobals[name] = StashValue.FromObject(value);
            }

            // 5. Execute in isolated VM (no debugger attached to avoid recursion)
            var tempVm = new VirtualMachine(tempGlobals, CancellationToken.None);
            tempVm.Output = _vm.Output;
            tempVm.ErrorOutput = _vm.ErrorOutput;
            tempVm.EmbeddedMode = true;
            object? result = tempVm.Execute(chunk);
            return (result, null);
        }
        catch (RuntimeError ex)
        {
            return (null, ex.Message);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
