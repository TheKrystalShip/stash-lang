namespace Stash.Interpreting.Types;

using System.Collections.Generic;
using Stash.Interpreting.Exceptions;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Represents a user-defined Stash function. Captures the declaration AST node
/// and the closure environment at the point of definition.
/// </summary>
public class StashFunction : UserCallable
{
    private readonly FnDeclStmt _declaration;

    public StashFunction(FnDeclStmt declaration, Environment closure)
        : base(closure)
    {
        _declaration = declaration;
    }

    protected override IReadOnlyList<Token> Parameters => _declaration.Parameters;
    protected override IReadOnlyList<Expr?> DefaultValues => _declaration.DefaultValues;
    protected override bool IsAsync => _declaration.IsAsync;
    protected override bool HasRestParam => _declaration.HasRestParam;
    protected override int LocalCount => _declaration.ResolvedLocalCount;

    public override Stash.Common.SourceSpan DefinitionSpan => _declaration.Span;

    public string Name => _declaration.Name.Lexeme;

    protected override object? ExecuteBody(Interpreter interpreter, Environment env)
    {
        try
        {
            interpreter.ExecuteBlock(_declaration.Body.Statements, env);
        }
        catch (ReturnException returnValue)
        {
            return returnValue.Value;
        }

        return null;
    }

    /// <summary>
    /// Calls this function with `self` bound to the given instance.
    /// Used for struct method dispatch. Creates two scope layers to match
    /// the resolver's topology: { self } → { params, body }.
    /// </summary>
    public override object? CallWithSelf(IInterpreterContext context, object instance, List<object?> arguments)
    {
        var interpreter = (Interpreter)context;
        var stashInstance = (StashInstance)instance;

        // Outer scope: binds 'self' (matches the resolver's BeginScope/Define("self"))
        var selfEnv = new Environment(Closure);
        selfEnv.Define("self", stashInstance);

        // Inner scope: binds parameters (matches ResolveFunction's BeginScope)
        var env = BindParameters(interpreter, arguments, selfEnv);

        if (IsAsync)
        {
            return RunAsync(interpreter, env);
        }

        return ExecuteBody(interpreter, env);
    }

    public override string ToString() => $"<fn {_declaration.Name.Lexeme}>";

    /// <summary>
    /// Calls this function with <c>self</c> bound to an arbitrary value.
    /// Used for extension methods on built-in types where <c>self</c> is not a <see cref="StashInstance"/>.
    /// </summary>
    public object? CallWithSelfValue(IInterpreterContext context, object? selfValue, List<object?> arguments)
    {
        var interpreter = (Interpreter)context;

        // Outer scope: binds 'self' to the receiver value (immutable for built-in types)
        var selfEnv = new Environment(Closure);
        selfEnv.DefineConstant("self", selfValue);

        // Inner scope: binds parameters
        var env = BindParameters(interpreter, arguments, selfEnv);

        if (IsAsync)
        {
            return RunAsync(interpreter, env);
        }

        return ExecuteBody(interpreter, env);
    }

    /// <summary>Creates a deep copy of this function with a snapshotted (deep-cloned) closure chain.</summary>
    public StashFunction DeepCopyWithSnapshot()
    {
        Environment snapshotClosure = Environment.Snapshot(Closure);
        return new StashFunction(_declaration, snapshotClosure);
    }

    /// <inheritdoc/>
    public override object DeepCopy() => DeepCopyWithSnapshot();
}
