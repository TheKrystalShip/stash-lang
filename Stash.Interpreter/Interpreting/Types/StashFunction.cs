namespace Stash.Interpreting.Types;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Interpreting.Exceptions;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Represents a user-defined Stash function. Captures the declaration AST node
/// and the closure environment at the point of definition.
/// </summary>
public class StashFunction : IStashCallable, IDeepCopyable
{
    private readonly FnDeclStmt _declaration;
    private readonly Environment _closure;

    public StashFunction(FnDeclStmt declaration, Environment closure)
    {
        _declaration = declaration;
        _closure = closure;
    }

    /// <summary>
    /// The source location where this function is defined.
    /// </summary>
    public Stash.Common.SourceSpan DefinitionSpan => _declaration.Span;

    Stash.Common.SourceSpan? IStashCallable.DefinitionSpan => _declaration.Span;

    public string Name => _declaration.Name.Lexeme;

    public int Arity => _declaration.Parameters.Count;

    public int MinArity
    {
        get
        {
            int required = 0;
            for (int i = 0; i < _declaration.DefaultValues.Count; i++)
            {
                if (_declaration.DefaultValues[i] == null)
                {
                    required++;
                }
                else
                {
                    break;
                }
            }
            return required;
        }
    }

    public object? Call(IInterpreterContext context, List<object?> arguments)
    {
        var interpreter = (Interpreter)context;
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

        if (_declaration.IsAsync)
        {
            return CallAsync(interpreter, env);
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

    private object? CallAsync(Interpreter interpreter, Environment env)
    {
        Environment snapshot = Environment.Snapshot(env);
        var cts = new CancellationTokenSource();

        var dotnetTask = Task.Run(() =>
        {
            Interpreter child = interpreter.Fork(snapshot, cts.Token);
            try
            {
                child.ExecuteBlock(_declaration.Body.Statements, snapshot);
                return (object?)null;
            }
            catch (ReturnException ret)
            {
                return ret.Value;
            }
            finally
            {
                child.CleanupTrackedProcesses();
            }
        });

        return new StashFuture(dotnetTask, cts);
    }

    /// <summary>
    /// Calls this function with `self` bound to the given instance.
    /// Used for struct method dispatch. Creates two scope layers to match
    /// the resolver's topology: { self } → { params, body }.
    /// </summary>
    public object? CallWithSelf(IInterpreterContext context, object instance, List<object?> arguments)
    {
        var interpreter = (Interpreter)context;
        var stashInstance = (StashInstance)instance;
        // Outer scope: binds 'self' (matches the resolver's BeginScope/Define("self"))
        var selfEnv = new Environment(_closure);
        selfEnv.Define("self", stashInstance);

        // Inner scope: binds parameters (matches ResolveFunction's BeginScope)
        var env = new Environment(selfEnv);

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

        if (_declaration.IsAsync)
        {
            Environment snapshot = Environment.Snapshot(env);
            var cts = new CancellationTokenSource();

            var dotnetTask = Task.Run(() =>
            {
                Interpreter child = interpreter.Fork(snapshot, cts.Token);
                try
                {
                    child.ExecuteBlock(_declaration.Body.Statements, snapshot);
                    return (object?)null;
                }
                catch (ReturnException ret)
                {
                    return ret.Value;
                }
                finally
                {
                    child.CleanupTrackedProcesses();
                }
            });

            return new StashFuture(dotnetTask, cts);
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

    /// <summary>Creates a deep copy of this function with a snapshotted (deep-cloned) closure chain.</summary>
    public StashFunction DeepCopyWithSnapshot()
    {
        Environment snapshotClosure = Environment.Snapshot(_closure);
        return new StashFunction(_declaration, snapshotClosure);
    }

    /// <inheritdoc/>
    public object DeepCopy() => DeepCopyWithSnapshot();
}
