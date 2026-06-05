namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

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

    /// <summary>Runs a function asynchronously in a new task and returns a Future. Use task.await() to wait for the result. If the function is itself an async function, its returned Future is unwrapped so the task resolves to the inner value rather than a Future-of-Future — task.run(async () =&gt; x) behaves like task.run(() =&gt; x).</summary>
    /// <param name="fn">The function to run asynchronously</param>
    /// <returns>A Future representing the running task</returns>
    [StashFn(ReturnType = "Future")]
    private static StashValue Run(IInterpreterContext ctx, IStashCallable fn)
    {
        var cts = ctx.CancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken)
            : new CancellationTokenSource();

        var dotnetTask = Task.Run<object?>(() =>
        {
            IInterpreterContext child = ctx.Fork(cts.Token);
            try
            {
                object? result = child.InvokeCallbackDirect(fn, ReadOnlySpan<StashValue>.Empty).ToObject();
                // Flatten an async fn's Future-of-Future: task.run(async () => x) resolves to x,
                // exactly like task.run(() => x). Only async callables are unwrapped — a plain
                // lambda that explicitly returns a Future keeps it (no surprise auto-flatten).
                return fn.IsAsync && result is StashFuture inner ? inner.GetResult() : result;
            }
            finally
            {
                child.CleanupTrackedProcesses();
            }
        }, cts.Token); // Pass the token so the Task transitions to Canceled (not Faulted) on OCE

        return StashValue.FromObj(new StashFuture(dotnetTask, cts));
    }

    /// <summary>Waits for a Future to complete and returns its result. Throws if the task failed.</summary>
    /// <param name="task">The Future to await</param>
    /// <exception cref="TypeError">if the argument is not a Future</exception>
    /// <exception cref="CancellationError">if the task was cancelled</exception>
    /// <returns>The result value of the task</returns>
    [StashFn(ReturnType = "any")]
    private static StashValue Await(IInterpreterContext ctx, StashValue task)
    {
        var future = GetFuture(task, "task.await");

        return StashValue.FromObject(future.GetResult());
    }

    /// <summary>
    /// Waits for all Futures in the array to complete and returns an array of per-element results in
    /// the same order. This is the <em>collect-all</em> combinator (analogous to
    /// <c>Promise.allSettled</c>): it never throws even if tasks fail.
    /// Failed tasks become <c>StashError</c> values with the <strong>original error type
    /// preserved</strong> (e.g. a task that throws <c>TypeError</c> produces a
    /// <c>StashError</c> whose <c>.type == "TypeError"</c>). Cancelled tasks become a
    /// <c>StashError</c> with <c>.type == "CancellationError"</c> and message
    /// <c>"Task was cancelled."</c>. Contrast with the <em>fail-fast</em> combinators
    /// (<c>task.all</c>, <c>task.race</c>, <c>task.awaitAny</c>) which throw on the
    /// first failure.
    /// </summary>
    /// <param name="tasks">An array of Futures</param>
    /// <exception cref="TypeError">if any element in the array is not a Future</exception>
    /// <returns>An array of result values; failed or cancelled elements are StashError values with the original error type</returns>
    [StashFn(ReturnType = "array")]
    private static StashValue AwaitAll(IInterpreterContext ctx, List<StashValue> tasks)
    {
        var futures = new List<StashFuture>(tasks.Count);
        foreach (StashValue item in tasks)
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

        // Collect results — failed/cancelled tasks become StashError values.
        // Route through f.GetResult() so the error type is identical to what `await` would throw
        // (CancellationError for cancelled futures, original RuntimeError subclass for faulted ones),
        // then convert to a StashError via FromRuntimeError to preserve .type and .message exactly.
        var results = new List<StashValue>(futures.Count);
        foreach (StashFuture f in futures)
        {
            if (f.IsFaulted || f.IsCancelled)
            {
                try
                {
                    f.GetResult(); // always throws for faulted/cancelled futures
                }
                catch (RuntimeError re)
                {
                    results.Add(StashValue.FromObj(StashError.FromRuntimeError(re, (List<string>?)null)));
                }
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
    /// <exception cref="ValueError">if the tasks array is empty</exception>
    /// <exception cref="TypeError">if any element in the array is not a Future</exception>
    /// <exception cref="CancellationError">if the first completed task was cancelled</exception>
    /// <returns>The result of the first completed Future</returns>
    [StashFn(ReturnType = "any")]
    private static StashValue AwaitAny(IInterpreterContext ctx, List<StashValue> tasks)
    {
        if (tasks.Count == 0)
        {
            throw new RuntimeError("task.awaitAny() expects a non-empty list.");
        }

        var futures = new List<StashFuture>(tasks.Count);
        foreach (StashValue item in tasks)
        {
            if (item.ToObject() is not StashFuture future)
            {
                throw new RuntimeError("First argument to 'task.awaitAny' must be a Future.");
            }

            futures.Add(future);
        }

        var dotnetTasks = futures.ConvertAll(f => (Task)f.DotNetTask);
        int idx = Task.WaitAny(dotnetTasks.ToArray());
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
    /// <exception cref="TypeError">if the argument is not a Future</exception>
    /// <returns>The task status enum value</returns>
    [StashFn(ReturnType = "string")]
    private static StashValue Status(IInterpreterContext ctx, StashValue task)
    {
        var future = GetFuture(task, "task.status");

        string statusStr = future.Status; // "Running", "Completed", "Failed", "Cancelled"
        return StashValue.FromObject(_taskStatusEnum.GetMember(statusStr));
    }

    /// <summary>Requests cancellation of a running Future. The task may not stop immediately.</summary>
    /// <param name="task">The Future to cancel</param>
    /// <exception cref="TypeError">if the argument is not a Future</exception>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void Cancel(IInterpreterContext ctx, StashValue task)
    {
        var future = GetFuture(task, "task.cancel");
        future.Cancel();
    }

    /// <summary>
    /// Returns a new Future that resolves when all Futures in the array complete. This is a
    /// <em>fail-fast</em> combinator (analogous to <c>Promise.all</c>): if any constituent
    /// task faults, the outer Future faults with the original error type; awaiting it throws
    /// that error. Plain values are wrapped in completed Futures. Contrast with
    /// <c>task.awaitAll</c> (collect-all).
    /// </summary>
    /// <param name="tasks">An array of Futures or plain values</param>
    /// <returns>A Future that resolves to an array of all results, or faults on the first failure</returns>
    [StashFn(ReturnType = "Future")]
    private static StashValue All(IInterpreterContext ctx, List<StashValue> tasks)
    {
        if (tasks.Count == 0)
        {
            return StashValue.FromObj(StashFuture.Resolved(new List<StashValue>()));
        }

        // Collect all .NET tasks
        var dotnetTasks = new List<Task<object?>>(tasks.Count);
        foreach (StashValue item in tasks)
        {
            if (item.ToObject() is StashFuture future)
            {
                dotnetTasks.Add(future.DotNetTask);
            }
            else
            {
                // Plain value — wrap in completed task
                dotnetTasks.Add(Task.FromResult(item.ToObject()));
            }
        }

        var cts = ctx.CancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken)
            : new CancellationTokenSource();
        var combinedTask = Task.Run(async () =>
        {
            await Task.WhenAll(dotnetTasks);
            var results = new List<StashValue>(dotnetTasks.Count);
            foreach (var t in dotnetTasks)
            {
                results.Add(StashValue.FromObject(t.Result));
            }
            return (object?)results;
        });

        return StashValue.FromObj(new StashFuture(combinedTask, cts));
    }

    /// <summary>
    /// Returns a new Future that resolves when the first Future in the array completes. This is a
    /// <em>fail-fast</em> combinator (analogous to <c>Promise.race</c>): if the winning task
    /// faults, the outer Future faults with the original error type; awaiting it throws that
    /// error. Requires a non-empty array.
    /// </summary>
    /// <param name="tasks">A non-empty array of Futures</param>
    /// <exception cref="ValueError">if the tasks array is empty</exception>
    /// <returns>A Future that resolves to the first completed value, or faults on the first failure</returns>
    [StashFn(ReturnType = "Future")]
    private static StashValue Race(IInterpreterContext ctx, List<StashValue> tasks)
    {
        if (tasks.Count == 0)
        {
            throw new RuntimeError("task.race() expects a non-empty array.");
        }

        var dotnetTasks = new List<Task<object?>>(tasks.Count);
        foreach (StashValue item in tasks)
        {
            if (item.ToObject() is StashFuture future)
            {
                dotnetTasks.Add(future.DotNetTask);
            }
            else
            {
                dotnetTasks.Add(Task.FromResult(item.ToObject()));
            }
        }

        var cts = ctx.CancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken)
            : new CancellationTokenSource();
        var raceTask = Task.Run(async () =>
        {
            Task<object?> winner = await Task.WhenAny(dotnetTasks);
            return await winner;
        });

        return StashValue.FromObj(new StashFuture(raceTask, cts));
    }

    /// <summary>Returns an already-resolved Future wrapping the given value.</summary>
    /// <param name="value">The value to resolve</param>
    /// <returns>A completed Future wrapping the value</returns>
    // Raw = true: 'value' is optional (0 or 1 args); typed form can't express truly optional StashValue.
    [StashFn(Raw = true, ReturnType = "Future")]
    private static StashValue Resolve(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return StashValue.FromObj(StashFuture.Resolved(args.Length > 0 ? args[0].ToObject() : null));
    }

    /// <summary>Returns a Future that completes after the given number of seconds.</summary>
    /// <param name="seconds">The delay duration in seconds</param>
    /// <returns>A Future that resolves to null after the delay</returns>
    [StashFn(ReturnType = "Future")]
    private static StashValue Delay(IInterpreterContext ctx, double seconds)
    {
        int ms = (int)(seconds * 1000);
        var cts = new CancellationTokenSource();
        var delayTask = Task.Run(async () =>
        {
            await Task.Delay(ms, cts.Token);
            return (object?)null;
        });

        return StashValue.FromObj(new StashFuture(delayTask, cts));
    }

    /// <summary>Executes a function with a timeout. Throws a TimeoutError if the function does not complete within the specified time. If the function is async, its returned Future is unwrapped and the timeout bounds the inner computation.</summary>
    /// <param name="ms">Timeout in milliseconds</param>
    /// <param name="fn">The function to execute</param>
    /// <exception cref="TimeoutError">if the function does not complete within the timeout</exception>
    /// <returns>The function's return value</returns>
    [StashFn(ReturnType = "any")]
    private static StashValue Timeout(IInterpreterContext ctx, [StashParam(Type = "number")] double ms, IStashCallable fn)
    {
        int timeoutMs = (int)ms;
        var cts = new CancellationTokenSource(timeoutMs);

        var task = Task.Run<object?>(() =>
        {
            IInterpreterContext child = ctx.Fork(cts.Token);
            try
            {
                object? result = child.InvokeCallbackDirect(fn, ReadOnlySpan<StashValue>.Empty).ToObject();
                // An async fn returns a Future; block on it here (the linked cts cancels the
                // inner computation when the timeout fires) so the timeout bounds the real work
                // rather than just the Future's creation. Mirrors the flatten in task.run.
                return fn.IsAsync && result is StashFuture inner ? inner.GetResult() : result;
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
                throw new TimeoutError($"Operation timed out after {timeoutMs}ms.");
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
            throw new TimeoutError($"Operation timed out after {timeoutMs}ms.");
        }
        catch (AggregateException ae) when (ae.InnerException is CancellationError)
        {
            // The child VM's outer dispatch loop converted the timeout-triggered OCE into
            // a CancellationError. From the caller's perspective this is a timeout, not an
            // external cancellation — re-tag it.
            throw new TimeoutError($"Operation timed out after {timeoutMs}ms.");
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

    private static StashFuture GetFuture(StashValue v, string funcName)
    {
        if (v.IsObj && v.AsObj is StashFuture f) return f;
        throw new RuntimeError($"First argument to '{funcName}' must be a future.");
    }
}
