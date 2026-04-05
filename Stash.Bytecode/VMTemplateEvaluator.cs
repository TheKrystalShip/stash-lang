namespace Stash.Bytecode;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;

/// <summary>
/// Implements <see cref="ITemplateEvaluator"/> for the bytecode VM.
/// Template "environments" are chains of <see cref="TemplateScope"/> dictionaries.
/// Each expression is compiled to a <c>return (&lt;expr&gt;);</c> program and executed
/// in a child <see cref="VirtualMachine"/> whose globals are the VM's globals merged
/// with the current template scope's variables.
/// </summary>
internal sealed class VMTemplateEvaluator : ITemplateEvaluator
{
    private readonly Dictionary<string, object?> _vmGlobals;

    internal VMTemplateEvaluator(Dictionary<string, object?> vmGlobals)
    {
        _vmGlobals = vmGlobals;
    }

    /// <summary>
    /// A scope in the template variable lookup chain.
    /// Inner scopes shadow outer scopes; the outermost scope's parent is null
    /// and <see cref="Globals"/> is the VM's global dictionary.
    /// </summary>
    private sealed class TemplateScope
    {
        internal readonly Dictionary<string, object?> Locals = new();
        internal readonly TemplateScope? Parent;
        internal readonly Dictionary<string, object?> Globals;

        internal TemplateScope(Dictionary<string, object?> globals, TemplateScope? parent = null)
        {
            Globals = globals;
            Parent = parent;
        }

        /// <summary>
        /// Flattens the full scope chain into a single dictionary.
        /// Inner scopes take precedence over outer scopes, which take precedence over globals.
        /// </summary>
        internal Dictionary<string, object?> Flatten()
        {
            var result = new Dictionary<string, object?>(Globals);
            ApplyLocals(result);
            return result;
        }

        private void ApplyLocals(Dictionary<string, object?> target)
        {
            Parent?.ApplyLocals(target);
            foreach (var (k, v) in Locals)
            {
                target[k] = v;
            }
        }
    }

    public object GlobalEnvironment => new TemplateScope(_vmGlobals);

    public object CreateChildEnvironment(object parent) =>
        new TemplateScope(_vmGlobals, (TemplateScope)parent);

    public void DefineVariable(object environment, string name, object? value) =>
        ((TemplateScope)environment).Locals[name] = value;

    public (object? Value, string? Error) EvaluateExpression(string expression, object environment)
    {
        try
        {
            var scope = (TemplateScope)environment;
            var evalGlobals = scope.Flatten();

            string program = "return (" + expression + ");";
            var lexer = new Lexer(program, "<template>");
            var tokens = lexer.ScanTokens();
            if (lexer.Errors.Count > 0)
            {
                return (null, lexer.Errors[0].ToString());
            }

            var parser = new Parser(tokens);
            var stmts = parser.ParseProgram();
            if (parser.Errors.Count > 0)
            {
                return (null, parser.Errors[0].ToString());
            }

            SemanticResolver.Resolve(stmts);
            var chunk = Compiler.Compile(stmts);
            var childVm = new VirtualMachine(evalGlobals);
            var result = childVm.Execute(chunk);
            return (result, null);
        }
        catch (RuntimeError e)
        {
            return (null, e.Message);
        }
        catch (Exception e)
        {
            return (null, e.Message);
        }
    }
}
