namespace Stash.Interpreting;

using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// Represents a Stash lambda (arrow function). Captures the lambda expression AST node
/// and the closure environment at the point of definition.
/// </summary>
public class StashLambda : IStashCallable
{
    private readonly LambdaExpr _declaration;
    private readonly Environment _closure;

    public StashLambda(LambdaExpr declaration, Environment closure)
    {
        _declaration = declaration;
        _closure = closure;
    }

    public int Arity => _declaration.Parameters.Count;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var env = new Environment(_closure);

        for (int i = 0; i < _declaration.Parameters.Count; i++)
        {
            env.Define(_declaration.Parameters[i].Lexeme, arguments[i]);
        }

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
}
