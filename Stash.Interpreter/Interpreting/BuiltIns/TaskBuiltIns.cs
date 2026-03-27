namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Interpreting;
using Stash.Interpreting.Types;
using StashEnv = Stash.Interpreting.Environment;

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
    internal static void Register(Stash.Interpreting.Environment globals, TaskRegistry registry)
    {
        var ns = new StashNamespace("task");

        ns.Define("run",        new BuiltInFunction("task.run",      1, Run));
        ns.Define("await",      new BuiltInFunction("task.await",    1, Await));
        ns.Define("awaitAll",   new BuiltInFunction("task.awaitAll", 1, AwaitAll));
        ns.Define("awaitAny",   new BuiltInFunction("task.awaitAny", 1, AwaitAny));
        ns.Define("status",     new BuiltInFunction("task.status",   1, (interp, args) => Status(interp, args, registry)));
        ns.Define("cancel",     new BuiltInFunction("task.cancel",   1, Cancel));
        ns.Define("Status", registry.TaskStatusEnum);

        ns.Define("all",     new BuiltInFunction("task.all",     1, All));
        ns.Define("race",    new BuiltInFunction("task.race",    1, Race));
        ns.Define("resolve", new BuiltInFunction("task.resolve", 1, TaskResolve));
        ns.Define("delay",   new BuiltInFunction("task.delay",   1, Delay));

        ns.Freeze();
        globals.Define("task", ns);
    }

    private static object? Run(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not IStashCallable callable)
        {
            throw new RuntimeError("task.run() expects a function argument.");
        }

        var cts = new CancellationTokenSource();
        StashEnv snapshot = StashEnv.Snapshot(interpreter._ctx.Environment);

        var dotnetTask = Task.Run(() =>
        {
            Interpreter child = interpreter.Fork(snapshot, cts.Token);
            try
            {
                return callable.Call(child, new List<object?>());
            }
            finally
            {
                child.CleanupTrackedProcesses();
            }
        });

        return new StashFuture(dotnetTask, cts);
    }

    private static object? Await(Interpreter interpreter, List<object?> args)
    {
        if (args[0] is not StashFuture future)
            throw new RuntimeError("First argument to 'task.await' must be a Future.");
        return future.GetResult();
    }

    private static object? AwaitAll(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not List<object?> items)
        {
            throw new RuntimeError("task.awaitAll() expects a list of Futures.");
        }

        var futures = new List<StashFuture>(items.Count);
        foreach (object? item in items)
        {
            if (item is not StashFuture future)
                throw new RuntimeError("First argument to 'task.awaitAll' must be a Future.");
            futures.Add(future);
        }

        // Wait for ALL tasks to complete before checking results
        foreach (StashFuture f in futures)
        {
            try { f.DotNetTask.GetAwaiter().GetResult(); }
            catch { /* Exceptions captured in task */ }
        }

        // Collect results — failed/cancelled tasks become StashError values
        var results = new List<object?>(futures.Count);
        foreach (StashFuture f in futures)
        {
            if (f.IsFaulted)
            {
                string? msg = null;
                try { f.DotNetTask.GetAwaiter().GetResult(); }
                catch (Exception ex) { msg = ex.Message; }
                results.Add(new StashError(msg ?? "Task failed.", "TaskError"));
            }
            else if (f.IsCancelled)
            {
                results.Add(new StashError("Task was cancelled.", "TaskCancelled"));
            }
            else
            {
                results.Add(f.DotNetTask.Result);
            }
        }

        return results;
    }

    private static object? AwaitAny(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not List<object?> items)
        {
            throw new RuntimeError("task.awaitAny() expects a list of Futures.");
        }

        if (items.Count == 0)
        {
            throw new RuntimeError("task.awaitAny() expects a non-empty list.");
        }

        var futures = new List<StashFuture>(items.Count);
        foreach (object? item in items)
        {
            if (item is not StashFuture future)
                throw new RuntimeError("First argument to 'task.awaitAny' must be a Future.");
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

        return completed.GetResult();
    }

    private static object? Status(Interpreter interpreter, List<object?> args, TaskRegistry registry)
    {
        if (args[0] is not StashFuture future)
            throw new RuntimeError("First argument to 'task.status' must be a Future.");
        string statusStr = future.Status; // "Running", "Completed", "Failed", "Cancelled"
        return registry.TaskStatusEnum.GetMember(statusStr);
    }

    private static object? Cancel(Interpreter interpreter, List<object?> args)
    {
        if (args[0] is not StashFuture future)
            throw new RuntimeError("First argument to 'task.cancel' must be a Future.");
        future.Cancel();
        return null;
    }

    private static object? All(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not List<object?> items)
        {
            throw new RuntimeError("task.all() expects an array of Futures.");
        }

        if (items.Count == 0)
        {
            return StashFuture.Resolved(new List<object?>());
        }

        // Collect all .NET tasks
        var tasks = new List<Task<object?>>(items.Count);
        foreach (object? item in items)
        {
            if (item is StashFuture future)
            {
                tasks.Add(future.DotNetTask);
            }
            else
            {
                // Plain value — wrap in completed task
                tasks.Add(Task.FromResult(item));
            }
        }

        var cts = new CancellationTokenSource();
        var combinedTask = Task.Run(async () =>
        {
            await Task.WhenAll(tasks);
            var results = new List<object?>(tasks.Count);
            foreach (var t in tasks)
            {
                results.Add(t.Result);
            }
            return (object?)results;
        });

        return new StashFuture(combinedTask, cts);
    }

    private static object? Race(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not List<object?> items)
        {
            throw new RuntimeError("task.race() expects an array of Futures.");
        }

        if (items.Count == 0)
        {
            throw new RuntimeError("task.race() expects a non-empty array.");
        }

        var tasks = new List<Task<object?>>(items.Count);
        foreach (object? item in items)
        {
            if (item is StashFuture future)
            {
                tasks.Add(future.DotNetTask);
            }
            else
            {
                tasks.Add(Task.FromResult(item));
            }
        }

        var cts = new CancellationTokenSource();
        var raceTask = Task.Run(async () =>
        {
            Task<object?> winner = await Task.WhenAny(tasks);
            return await winner;
        });

        return new StashFuture(raceTask, cts);
    }

    private static object? TaskResolve(Interpreter interpreter, List<object?> args)
    {
        return StashFuture.Resolved(args.Count > 0 ? args[0] : null);
    }

    private static object? Delay(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1)
        {
            throw new RuntimeError("task.delay() expects a number (seconds).");
        }

        double seconds = args[0] switch
        {
            long l => l,
            double d => d,
            _ => throw new RuntimeError("task.delay() expects a number (seconds).")
        };

        int ms = (int)(seconds * 1000);
        var cts = new CancellationTokenSource();
        var delayTask = Task.Run(async () =>
        {
            await Task.Delay(ms, cts.Token);
            return (object?)null;
        });

        return new StashFuture(delayTask, cts);
    }
}

