namespace Stash.Interpreting.Types;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Interpreting.Exceptions;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Abstract base for user-defined callables (functions and lambdas).
/// Encapsulates shared closure capture, parameter binding, async dispatch, and deep-copy logic.
/// </summary>
public abstract class UserCallable : IStashCallable, IDeepCopyable
{
    protected readonly Environment Closure;

    protected UserCallable(Environment closure)
    {
        Closure = closure;
    }

    protected abstract IReadOnlyList<Token> Parameters { get; }
    protected abstract IReadOnlyList<Expr?> DefaultValues { get; }
    protected abstract bool IsAsync { get; }

    public abstract SourceSpan DefinitionSpan { get; }

    SourceSpan? IStashCallable.DefinitionSpan => DefinitionSpan;

    public int Arity => Parameters.Count;

    public int MinArity
    {
        get
        {
            int required = 0;
            for (int i = 0; i < DefaultValues.Count; i++)
            {
                if (DefaultValues[i] == null)
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

    /// <summary>
    /// Binds arguments to parameter names in a new scope.
    /// When <paramref name="parentOverride"/> is provided, uses it instead of the closure as parent scope.
    /// </summary>
    protected Environment BindParameters(Interpreter interpreter, List<object?> arguments, Environment? parentOverride = null)
    {
        var env = new Environment(parentOverride ?? Closure);

        for (int i = 0; i < Parameters.Count; i++)
        {
            object? value;
            if (i < arguments.Count)
            {
                value = arguments[i];
            }
            else
            {
                value = DefaultValues[i]!.Accept(interpreter);
            }
            env.Define(Parameters[i].Lexeme, value);
        }

        return env;
    }

    public virtual object? Call(IInterpreterContext context, List<object?> arguments)
    {
        var interpreter = (Interpreter)context;
        var env = BindParameters(interpreter, arguments);

        if (IsAsync)
        {
            return RunAsync(interpreter, env);
        }

        return ExecuteBody(interpreter, env);
    }

    public virtual object? CallWithSelf(IInterpreterContext context, object instance, List<object?> arguments)
        => Call(context, arguments);

    /// <summary>
    /// Runs the callable asynchronously by forking the interpreter and snapshotting the environment.
    /// </summary>
    protected object? RunAsync(Interpreter interpreter, Environment env)
    {
        Environment snapshot = Environment.Snapshot(env);
        var cts = new CancellationTokenSource();

        var dotnetTask = Task.Run(() =>
        {
            Interpreter child = interpreter.Fork(snapshot, cts.Token);
            try
            {
                return ExecuteBody(child, snapshot);
            }
            finally
            {
                child.CleanupTrackedProcesses();
            }
        });

        return new StashFuture(dotnetTask, cts);
    }

    /// <summary>
    /// Executes the callable's body in the given environment. Subclasses handle
    /// block bodies, expression bodies, and ReturnException catching.
    /// </summary>
    protected abstract object? ExecuteBody(Interpreter interpreter, Environment env);

    public abstract object DeepCopy();
}
