namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the 'task' namespace built-in functions for parallel task execution.
/// </summary>
[StashNamespace]
public static partial class TaskBuiltIns
{
    /// <summary>Status enum for task futures.</summary>
    [StashEnum(Name = "Status")]
    public enum TaskStatusValue { Running, Completed, Failed, Cancelled }

    private static readonly StashEnum _taskStatusEnum = new("Status", new List<string> { "Running", "Completed", "Failed", "Cancelled" });

    /// <summary>Runs a function asynchronously in a new task and returns a Future. Use task.await() to wait for the result.</summary>
    /// <param name="fn">The function to run asynchronously</param>
    /// <returns>A Future representing the running task</returns>
    [StashFn(Raw = true, ReturnType = "Future")]
    private static StashValue Run(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var callable = SvArgs.Callable(args, 0, "task.run");

        var cts = ctx.CancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken)
            : new CancellationTokenSource();

        var dotnetTask = Task.Run<object?>(() =>
        {
            IInterpreterContext child = ctx.Fork(cts.Token);
            try
            {
                return child.InvokeCallbackDirect(callable, ReadOnlySpan<StashValue>.Empty).ToObject();
            }
            finally
            {
                child.CleanupTrackedProcesses();
            }
        });

        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    /// <summary>Waits for a Future to complete and returns its result. Throws if the task failed.</summary>
    /// <param name="task">The Future to await</param>
    /// <returns>The result value of the task</returns>
    [StashFn(Raw = true, ReturnType = "any")]
    private static StashValue Await(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var future = SvArgs.Future(args, 0, "task.await");

        return StashValue.FromObject(future.GetResult());
    }

    /// <summary>Waits for all Futures in the array to complete. Returns an array of results in the same order. Failed tasks become error values.</summary>
    /// <param name="tasks">An array of Futures</param>
    /// <returns>An array of result values</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue AwaitAll(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var items = SvArgs.StashList(args, 0, "task.awaitAll");

        var futures = new List<StashFuture>(items.Count);
        foreach (StashValue item in items)
        {
            if (item.ToObject() is not StashFuture future)
            {
                throw new RuntimeError("First argument to 'task.awaitAll' must be a Future.");
            }

            futures.Add(future);
        }

        // Wait for ALL tasks to complete before checking results
        foreach (StashFuture f in futures)
        {
            try { f.DotNetTask.GetAwaiter().GetResult(); }
            catch { /* Exceptions captured in task */ }
        }

        // Collect results — failed/cancelled tasks become StashError values
        var results = new List<StashValue>(futures.Count);
        foreach (StashFuture f in futures)
        {
            if (f.IsFaulted)
            {
                string? msg = null;
                try { f.DotNetTask.GetAwaiter().GetResult(); }
                catch (Exception ex) { msg = ex.Message; }
                results.Add(StashValue.FromObj(new StashError(msg ?? "Task failed.", "TaskError")));
            }
            else if (f.IsCancelled)
            {
                results.Add(StashValue.FromObj(new StashError("Task was cancelled.", "TaskCancelled")));
            }
            else
            {
                results.Add(StashValue.FromObject(f.DotNetTask.Result));
            }
        }

        return StashValue.FromObj(results);
    }

    /// <summary>Waits for the first Future in the array to complete. Returns its result and cancels all remaining tasks.</summary>
    /// <param name="tasks">A non-empty array of Futures</param>
    /// <returns>The result of the first completed Future</returns>
    [StashFn(Raw = true, ReturnType = "any")]
    private static StashValue AwaitAny(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var items = SvArgs.StashList(args, 0, "task.awaitAny");

        if (items.Count == 0)
        {
            throw new RuntimeError("task.awaitAny() expects a non-empty list.");
        }

        var futures = new List<StashFuture>(items.Count);
        foreach (StashValue item in items)
        {
            if (item.ToObject() is not StashFuture future)
            {
                throw new RuntimeError("First argument to 'task.awaitAny' must be a Future.");
            }

            futures.Add(future);
        }

        var tasks = futures.ConvertAll(f => (Task)f.DotNetTask);
        int idx = Task.WaitAny(tasks.ToArray());
        StashFuture completed = futures[idx];

        // Cancel remaining futures
        for (int i = 0; i < futures.Count; i++)
        {
            if (i != idx)
            {
                try { futures[i].Cancel(); } catch (ObjectDisposedException) { }
            }
        }

        return StashValue.FromObject(completed.GetResult());
    }

    /// <summary>Returns the current status of a Future as a Status enum value: Running, Completed, Failed, or Cancelled.</summary>
    /// <param name="task">The Future to check</param>
    /// <returns>The task status enum value</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Status(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var future = SvArgs.Future(args, 0, "task.status");

        string statusStr = future.Status; // "Running", "Completed", "Failed", "Cancelled"
        return StashValue.FromObject(_taskStatusEnum.GetMember(statusStr));
    }

    /// <summary>Requests cancellation of a running Future. The task may not stop immediately.</summary>
    /// <param name="task">The Future to cancel</param>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Cancel(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var future = SvArgs.Future(args, 0, "task.cancel");

        future.Cancel();
        return StashValue.Null;
    }

    /// <summary>Returns a new Future that resolves when all Futures in the array complete. Plain values are wrapped in completed Futures.</summary>
    /// <param name="tasks">An array of Futures or plain values</param>
    /// <returns>A Future that resolves to an array of all results</returns>
    [StashFn(Raw = true, ReturnType = "Future")]
    private static StashValue All(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var items = SvArgs.StashList(args, 0, "task.all");

        if (items.Count == 0)
        {
            return StashValue.FromObj(StashFuture.Resolved(new List<StashValue>()));
        }

        // Collect all .NET tasks
        var tasks = new List<Task<object?>>(items.Count);
        foreach (StashValue item in items)
        {
            if (item.ToObject() is StashFuture future)
            {
                tasks.Add(future.DotNetTask);
            }
            else
            {
                // Plain value — wrap in completed task
                tasks.Add(Task.FromResult(item.ToObject()));
            }
        }

        var cts = ctx.CancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken)
            : new CancellationTokenSource();
        var combinedTask = Task.Run(async () =>
        {
            await Task.WhenAll(tasks);
            var results = new List<StashValue>(tasks.Count);
            foreach (var t in tasks)
            {
                results.Add(StashValue.FromObject(t.Result));
            }
            return (object?)results;
        });

        return StashValue.FromObj(new StashFuture(combinedTask, cts));
    }

    /// <summary>Returns a new Future that resolves when the first Future in the array completes. Requires a non-empty array.</summary>
    /// <param name="tasks">A non-empty array of Futures</param>
    /// <returns>A Future that resolves to the first completed value</returns>
    [StashFn(Raw = true, ReturnType = "Future")]
    private static StashValue Race(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var items = SvArgs.StashList(args, 0, "task.race");

        if (items.Count == 0)
        {
            throw new RuntimeError("task.race() expects a non-empty array.");
        }

        var tasks = new List<Task<object?>>(items.Count);
        foreach (StashValue item in items)
        {
            if (item.ToObject() is StashFuture future)
            {
                tasks.Add(future.DotNetTask);
            }
            else
            {
                tasks.Add(Task.FromResult(item.ToObject()));
            }
        }

        var cts = ctx.CancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken)
            : new CancellationTokenSource();
        var raceTask = Task.Run(async () =>
        {
            Task<object?> winner = await Task.WhenAny(tasks);
            return await winner;
        });

        return StashValue.FromObj(new StashFuture(raceTask, cts));
    }

    /// <summary>Returns an already-resolved Future wrapping the given value.</summary>
    /// <param name="value">The value to resolve</param>
    /// <returns>A completed Future wrapping the value</returns>
    [StashFn(Raw = true, ReturnType = "Future")]
    private static StashValue Resolve(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return StashValue.FromObj(StashFuture.Resolved(args.Length > 0 ? args[0].ToObject() : null));
    }

    /// <summary>Returns a Future that completes after the given number of seconds.</summary>
    /// <param name="seconds">The delay duration in seconds</param>
    /// <returns>A Future that resolves to null after the delay</returns>
    [StashFn(Raw = true, ReturnType = "Future")]
    private static StashValue Delay(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var seconds = SvArgs.Numeric(args, 0, "task.delay");

        int ms = (int)(seconds * 1000);
        var cts = new CancellationTokenSource();
        var delayTask = Task.Run(async () =>
        {
            await Task.Delay(ms, cts.Token);
            return (object?)null;
        });

        return StashValue.FromObj(new StashFuture(delayTask, cts));
    }

    /// <summary>Executes a function with a timeout. Throws a TimeoutError if the function does not complete within the specified time.</summary>
    /// <param name="ms">Timeout in milliseconds</param>
    /// <param name="fn">The function to execute</param>
    /// <returns>The function's return value</returns>
    [StashFn(Raw = true, ReturnType = "any")]
    private static StashValue Timeout(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        double ms = SvArgs.Numeric(args, 0, "task.timeout");
        var callable = SvArgs.Callable(args, 1, "task.timeout");

        int timeoutMs = (int)ms;
        var cts = new CancellationTokenSource(timeoutMs);

        var task = Task.Run<object?>(() =>
        {
            IInterpreterContext child = ctx.Fork(cts.Token);
            try
            {
                return child.InvokeCallbackDirect(callable, ReadOnlySpan<StashValue>.Empty).ToObject();
            }
            finally
            {
                child.CleanupTrackedProcesses();
            }
        });

        try
        {
            bool completed = task.Wait(timeoutMs);
            if (!completed)
            {
                cts.Cancel();
                throw new RuntimeError($"Operation timed out after {timeoutMs}ms.", errorType: StashErrorTypes.TimeoutError);
            }

            // Check if the task faulted
            if (task.IsFaulted)
            {
                Exception inner = task.Exception!.InnerException!;
                if (inner is RuntimeError re)
                    throw re;
                throw new RuntimeError(inner.Message);
            }

            return StashValue.FromObject(task.Result);
        }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
        {
            throw new RuntimeError($"Operation timed out after {timeoutMs}ms.", errorType: StashErrorTypes.TimeoutError);
        }
        catch (AggregateException ae) when (ae.InnerException is RuntimeError re && re.ErrorType == StashErrorTypes.CancellationError)
        {
            // The child VM's outer dispatch loop converted the timeout-triggered OCE into
            // a CancellationError. From the caller's perspective this is a timeout, not an
            // external cancellation — re-tag it.
            throw new RuntimeError($"Operation timed out after {timeoutMs}ms.", errorType: StashErrorTypes.TimeoutError);
        }
        catch (AggregateException ae) when (ae.InnerException is RuntimeError re)
        {
            throw re;
        }
        catch (AggregateException ae) when (ae.InnerException is not null)
        {
            throw new RuntimeError(ae.InnerException.Message);
        }
    }
}

