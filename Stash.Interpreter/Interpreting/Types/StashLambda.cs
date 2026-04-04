namespace Stash.Interpreting.Types;

using System.Collections.Generic;
using Stash.Interpreting.Exceptions;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;

/// <summary>
/// Represents a Stash lambda (arrow function). Captures the lambda expression AST node
/// and the closure environment at the point of definition.
/// </summary>
public class StashLambda : UserCallable
{
    private readonly LambdaExpr _declaration;

    public StashLambda(LambdaExpr declaration, Environment closure)
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

    protected override object? ExecuteBody(Interpreter interpreter, Environment env)
    {
        if (_declaration.ExpressionBody != null)
        {
            return interpreter.EvaluateInEnvironment(_declaration.ExpressionBody, env);
        }

        try
        {
            interpreter.ExecuteBlock(_declaration.BlockBody!.Statements, env);
        }
        catch (ReturnException returnValue)
        {
            return returnValue.Value;
        }

        return null;
    }

    public override string ToString() => "<lambda>";

    /// <summary>Creates a deep copy of this lambda with a snapshotted (deep-cloned) closure chain.</summary>
    public StashLambda DeepCopyWithSnapshot()
    {
        Environment snapshotClosure = Environment.Snapshot(Closure);
        return new StashLambda(_declaration, snapshotClosure);
    }

    /// <inheritdoc/>
    public override object DeepCopy() => DeepCopyWithSnapshot();
}
