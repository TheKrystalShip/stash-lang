namespace Stash.Interpreting.Types;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Represents a future value in the Stash runtime — the result of calling an async function.
/// Wraps a .NET <see cref="Task{T}"/> and provides blocking resolution via <see cref="GetResult"/>.
/// </summary>
public class StashFuture
{
    private readonly Task<object?> _task;
    private readonly CancellationTokenSource _cts;

    /// <summary>
    /// Creates a new StashFuture wrapping the given .NET task.
    /// </summary>
    public StashFuture(Task<object?> task, CancellationTokenSource cts)
    {
        _task = task;
        _cts = cts;
    }

    /// <summary>
    /// Creates an already-resolved future with the given value.
    /// </summary>
    public static StashFuture Resolved(object? value)
    {
        return new StashFuture(Task.FromResult(value), new CancellationTokenSource());
    }

    /// <summary>
    /// Creates an already-failed future with the given error message.
    /// </summary>
    public static StashFuture Failed(string message, string? errorType = null)
    {
        var tcs = new TaskCompletionSource<object?>();
        tcs.SetException(new Stash.Interpreting.RuntimeError(message, errorType: errorType));
        return new StashFuture(tcs.Task, new CancellationTokenSource());
    }

    /// <summary>
    /// Blocks until the future resolves and returns the result.
    /// Throws <see cref="Stash.Interpreting.RuntimeError"/> if the async operation failed or was cancelled.
    /// </summary>
    public object? GetResult()
    {
        try
        {
            return _task.GetAwaiter().GetResult();
        }
        catch (Stash.Interpreting.RuntimeError)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new Stash.Interpreting.RuntimeError("Future was cancelled.");
        }
        catch (AggregateException ae) when (ae.InnerException is Stash.Interpreting.RuntimeError re)
        {
            throw re;
        }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
        {
            throw new Stash.Interpreting.RuntimeError("Future was cancelled.");
        }
        catch (Exception ex)
        {
            throw new Stash.Interpreting.RuntimeError($"Future failed: {ex.Message}");
        }
    }

    /// <summary>Gets whether the future has completed (successfully, faulted, or cancelled).</summary>
    public bool IsCompleted => _task.IsCompleted;

    /// <summary>Gets whether the future completed with an error.</summary>
    public bool IsFaulted => _task.IsFaulted;

    /// <summary>Gets whether the future was cancelled.</summary>
    public bool IsCancelled => _task.IsCanceled;

    /// <summary>Requests cancellation of the underlying operation.</summary>
    public void Cancel() => _cts.Cancel();

    /// <summary>Gets the underlying .NET Task (for composition in task.all/task.race).</summary>
    internal Task<object?> DotNetTask => _task;

    /// <summary>Gets the current status as a display string.</summary>
    public string Status
    {
        get
        {
            if (_task.IsCanceled)
            {
                return "Cancelled";
            }

            if (_task.IsFaulted)
            {
                return "Failed";
            }

            if (_task.IsCompleted)
            {
                return "Completed";
            }

            return "Running";
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"<Future:{Status}>";
}
