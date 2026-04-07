namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the 'task' namespace built-in functions for parallel task execution.
/// </summary>
public static class TaskBuiltIns
{
    /// <summary>
    /// Registers all <c>task</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Environment"/> to register functions in.</param>
    /// <param name="registry">The task registry holding the task.Status enum.</param>
    public static NamespaceDefinition Define()
    {
        var taskStatusEnum = new StashEnum("Status", new List<string> { "Running", "Completed", "Failed", "Cancelled" });
        var ns = new NamespaceBuilder("task");

        ns.Function("run",      [Param("fn", "function")], Run);
        ns.Function("await",    [Param("task", "Future")], Await);
        ns.Function("awaitAll", [Param("tasks", "array")], AwaitAll);
        ns.Function("awaitAny", [Param("tasks", "array")], AwaitAny);
        ns.Function("status",   [Param("task", "Future")], (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) => Status(ctx, args, taskStatusEnum));
        ns.Function("cancel",   [Param("task", "Future")], Cancel);
        ns.Function("all",     [Param("tasks", "array")], All);
        ns.Function("race",    [Param("tasks", "array")], Race);
        ns.Function("resolve", [Param("value")], TaskResolve);
        ns.Function("delay",   [Param("seconds", "number")], Delay);

        ns.Enum("Status", ["Running", "Completed", "Failed", "Cancelled"]);

        return ns.Build();
    }

    private static StashValue Run(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var callable = SvArgs.Callable(args, 0, "task.run");

        var cts = new CancellationTokenSource();

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

    private static StashValue Await(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var future = SvArgs.Future(args, 0, "task.await");

        return StashValue.FromObject(future.GetResult());
    }

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

    private static StashValue Status(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, StashEnum taskStatusEnum)
    {
        var future = SvArgs.Future(args, 0, "task.status");

        string statusStr = future.Status; // "Running", "Completed", "Failed", "Cancelled"
        return StashValue.FromObject(taskStatusEnum.GetMember(statusStr));
    }

    private static StashValue Cancel(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var future = SvArgs.Future(args, 0, "task.cancel");

        future.Cancel();
        return StashValue.Null;
    }

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

        var cts = new CancellationTokenSource();
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

        var cts = new CancellationTokenSource();
        var raceTask = Task.Run(async () =>
        {
            Task<object?> winner = await Task.WhenAny(tasks);
            return await winner;
        });

        return StashValue.FromObj(new StashFuture(raceTask, cts));
    }

    private static StashValue TaskResolve(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return StashValue.FromObj(StashFuture.Resolved(args.Length > 0 ? args[0].ToObject() : null));
    }

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
}

