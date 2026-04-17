namespace Stash.Runtime.Types;

using System;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents a future value in the Stash runtime — the result of calling an async function.
/// </summary>
public class StashFuture : IVMTyped, IVMStringifiable
{
    private readonly Task<object?> _task;
    private readonly CancellationTokenSource _cts;

    public StashFuture(Task<object?> task, CancellationTokenSource cts)
    {
        _task = task;
        _cts = cts;
    }

    public static StashFuture Resolved(object? value)
    {
        return new StashFuture(Task.FromResult(value), new CancellationTokenSource());
    }

    public static StashFuture Failed(string message, string? errorType = null)
    {
        var tcs = new TaskCompletionSource<object?>();
        tcs.SetException(new RuntimeError(message, errorType: errorType));
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
            throw new RuntimeError("Future was cancelled.");
        }
        catch (AggregateException ae) when (ae.InnerException is RuntimeError re)
        {
            throw re;
        }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
        {
            throw new RuntimeError("Future was cancelled.");
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

    public string VMTypeName => "Future";

    public string VMToString() => ToString();
}
