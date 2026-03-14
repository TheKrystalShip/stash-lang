namespace Stash.Interpreting.Types;

using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// Represents a user-defined Stash function. Captures the declaration AST node
/// and the closure environment at the point of definition.
/// </summary>
public class StashFunction : IStashCallable
{
    private readonly FnDeclStmt _declaration;
    private readonly Environment _closure;

    public StashFunction(FnDeclStmt declaration, Environment closure)
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

    public override string ToString() => $"<fn {_declaration.Name.Lexeme}>";
}
