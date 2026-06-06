namespace Stash.Runtime.Types;

using System;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime.Errors;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents a future value in the Stash runtime — the result of calling an async function.
/// </summary>
public class StashFuture : IVMTyped, IVMStringifiable, IVMPrimitiveType
{
    private readonly Task<object?> _task;
    private readonly CancellationTokenSource _cts;

    /// <summary>
    /// True if this future's outcome has been consumed by a registered consumer
    /// (await, task.await, task.awaitAll, task.awaitAny, task.all, task.race).
    /// When false at script exit, D1 will report the fault (if faulted &amp;&amp; !cancelled).
    /// </summary>
    public bool Observed { get; private set; }

    /// <summary>
    /// Marks this future's outcome as observed — called by every outcome-consuming
    /// combinator so D1 does not false-report a normally-awaited future at exit.
    /// Idempotent: subsequent calls are no-ops.
    /// </summary>
    public void MarkObserved() => Observed = true;

    public StashFuture(Task<object?> task, CancellationTokenSource cts)
    {
        _task = task;
        _cts = cts;
    }

    public static StashFuture Resolved(object? value)
    {
        return new StashFuture(Task.FromResult(value), new CancellationTokenSource());
    }

    public static StashFuture Failed(string message)
    {
        var tcs = new TaskCompletionSource<object?>();
        tcs.SetException(new RuntimeError(message));
        return new StashFuture(tcs.Task, new CancellationTokenSource());
    }

    /// <summary>
    /// Creates an already-faulted future with the given error type name and message.
    /// Used in tests to construct pre-faulted futures without timing-dependent background tasks.
    /// </summary>
    public static StashFuture Failed(string errorType, string message)
    {
        var tcs = new TaskCompletionSource<object?>();
        tcs.SetException(new Errors.UserRuntimeError(errorType, message));
        return new StashFuture(tcs.Task, new CancellationTokenSource());
    }

    public object? GetResult()
    {
        try
        {
            return _task.GetAwaiter().GetResult();
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new CancellationError("Task was cancelled.");
        }
        catch (AggregateException ae) when (ae.InnerException is RuntimeError re)
        {
            throw re;
        }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
        {
            throw new CancellationError("Task was cancelled.");
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Future failed: {ex.Message}");
        }
    }

    public bool IsCompleted => _task.IsCompleted;
    public bool IsFaulted => _task.IsFaulted;
    public bool IsCancelled => _task.IsCanceled;

    public void Cancel() => _cts.Cancel();

    public Task<object?> DotNetTask => _task;

    public string Status
    {
        get
        {
            if (_task.IsCanceled) return "Cancelled";
            if (_task.IsFaulted) return "Failed";
            if (_task.IsCompleted) return "Completed";
            return "Running";
        }
    }

    public override string ToString() => $"<Future:{Status}>";

    // --- VM Protocol Implementations ---

    public static string PrimitiveTypeName => "Future";
    public static string PrimitiveTypeDescription =>
        "Represents an asynchronous computation that may not have completed yet. " +
        "Returned by async functions. Use `await` to get the resolved value.";

    public string VMTypeName => PrimitiveTypeName;

    public string VMToString() => ToString();
}
