namespace Stash.Interpreting.Types;

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

    /// <summary>
    /// The source location where this lambda is defined.
    /// </summary>
    public Stash.Common.SourceSpan DefinitionSpan => _declaration.Span;

    public int Arity => _declaration.Parameters.Count;

    public int MinArity
    {
        get
        {
            int required = 0;
            for (int i = 0; i < _declaration.DefaultValues.Count; i++)
            {
                if (_declaration.DefaultValues[i] == null)
                    required++;
                else
                    break;
            }
            return required;
        }
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var env = new Environment(_closure);

        for (int i = 0; i < _declaration.Parameters.Count; i++)
        {
            object? value;
            if (i < arguments.Count)
            {
                value = arguments[i];
            }
            else
            {
                value = _declaration.DefaultValues[i]!.Accept(interpreter);
            }
            env.Define(_declaration.Parameters[i].Lexeme, value);
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
