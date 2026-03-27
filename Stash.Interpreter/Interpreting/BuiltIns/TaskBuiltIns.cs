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
/// Task state is scoped to the interpreter's <see cref="TaskRegistry"/> — each
/// independent interpreter instance gets its own task registry, while forked
/// children share the parent's registry.
/// </summary>
public static class TaskBuiltIns
{
    /// <summary>
    /// Registers all <c>task</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Environment"/> to register functions in.</param>
    internal static void Register(Stash.Interpreting.Environment globals, TaskRegistry registry)
    {
        var ns = new StashNamespace("task");

        ns.Define("run",        new BuiltInFunction("task.run",      1, Run));
        ns.Define("await",      new BuiltInFunction("task.await",    1, Await));
        ns.Define("awaitAll",   new BuiltInFunction("task.awaitAll", 1, AwaitAll));
        ns.Define("awaitAny",   new BuiltInFunction("task.awaitAny", 1, AwaitAny));
        ns.Define("status",     new BuiltInFunction("task.status",   1, Status));
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

        TaskRegistry registry = interpreter.TaskRegistry;

        long id = registry.NextId();
        var cts = new CancellationTokenSource();

        StashEnumValue runningStatus   = registry.TaskStatusEnum.GetMember("Running")!;
        StashEnumValue completedStatus = registry.TaskStatusEnum.GetMember("Completed")!;
        StashEnumValue failedStatus    = registry.TaskStatusEnum.GetMember("Failed")!;
        StashEnumValue cancelledStatus = registry.TaskStatusEnum.GetMember("Cancelled")!;

        StashEnv snapshot = StashEnv.Snapshot(interpreter._ctx.Environment);

        var handle = new StashInstance("TaskHandle", new Dictionary<string, object?>
        {
            ["id"]     = id,
            ["status"] = runningStatus
        });

        var state = new TaskRegistry.TaskState { Cts = cts, Status = runningStatus };

        int debugThreadId = registry.NextDebugThreadId();

        Task task = Task.Run(() =>
        {
            Interpreter child = interpreter.Fork(snapshot, cts.Token);
            child.DebugThreadId = debugThreadId;
            interpreter.Debugger?.OnThreadStarted(debugThreadId, $"Task {id}", child);
            try
            {
                object? result = callable.Call(child, new List<object?>());
                state.Result = result;
                state.Status = completedStatus;
            }
            catch (OperationCanceledException)
            {
                state.Status = cancelledStatus;
            }
            catch (RuntimeError ex)
            {
                state.Error = ex.Message;
                state.Status = failedStatus;
            }
            catch (Exception ex)
            {
                state.Error = ex.ToString();
                state.Status = failedStatus;
            }
            finally
            {
                child.CleanupTrackedProcesses();
                interpreter.Debugger?.OnThreadExited(debugThreadId);
            }
        });

        state.DotNetTask = task;
        registry.Tasks[id] = state;

        return handle;
    }

    private static object? Await(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not StashInstance handle || handle.TypeName != "TaskHandle")
        {
            throw new RuntimeError("task.await() expects a TaskHandle.");
        }

        TaskRegistry registry = interpreter.TaskRegistry;

        long id = GetTaskId(handle);
        if (!registry.Tasks.TryGetValue(id, out TaskRegistry.TaskState? state))
        {
            throw new RuntimeError("task.await(): invalid or unknown task handle.");
        }

        try
        {
            state.DotNetTask.GetAwaiter().GetResult();
        }
        catch
        {
            // Exceptions are captured in state, not propagated through the Task wrapper.
        }

        registry.Tasks.TryRemove(id, out _);
        state.Cts.Dispose();
        handle.SetField("status", state.Status, null);

        if (state.Status.MemberName == "Failed")
        {
            throw new RuntimeError(state.Error ?? "Task failed.");
        }

        if (state.Status.MemberName == "Cancelled")
        {
            throw new RuntimeError("Task was cancelled.");
        }

        return state.Result;
    }

    private static object? AwaitAll(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not List<object?> handles)
        {
            throw new RuntimeError("task.awaitAll() expects a list of task handles.");
        }

        TaskRegistry registry = interpreter.TaskRegistry;

        // Validate and collect all task states upfront
        var entries = new List<(StashInstance Handle, TaskRegistry.TaskState State, long Id)>();
        foreach (object? h in handles)
        {
            if (h is not StashInstance taskHandle || taskHandle.TypeName != "TaskHandle")
            {
                throw new RuntimeError("task.awaitAll() expects TaskHandle values.");
            }

            long id = GetTaskId(taskHandle);
            if (!registry.Tasks.TryGetValue(id, out TaskRegistry.TaskState? state))
            {
                throw new RuntimeError("task.awaitAll(): invalid or unknown task handle.");
            }

            entries.Add((taskHandle, state, id));
        }

        // Wait for ALL tasks to complete before checking results
        foreach (var (_, state, _) in entries)
        {
            try { state.DotNetTask.GetAwaiter().GetResult(); }
            catch { /* Exceptions captured in state */ }
        }

        // Clean up ALL tasks and collect results — failed/cancelled tasks become StashError values
        var results = new List<object?>();

        foreach (var (handle, state, id) in entries)
        {
            registry.Tasks.TryRemove(id, out _);
            state.Cts.Dispose();
            handle.SetField("status", state.Status, null);

            if (state.Status.MemberName == "Failed")
            {
                results.Add(new StashError(state.Error ?? "Task failed.", "TaskError"));
            }
            else if (state.Status.MemberName == "Cancelled")
            {
                results.Add(new StashError("Task was cancelled.", "TaskCancelled"));
            }
            else
            {
                results.Add(state.Result);
            }
        }

        return results;
    }

    private static object? AwaitAny(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not List<object?> handles)
        {
            throw new RuntimeError("task.awaitAny() expects a list of task handles.");
        }

        if (handles.Count == 0)
        {
            throw new RuntimeError("task.awaitAny() expects a non-empty list.");
        }

        TaskRegistry registry = interpreter.TaskRegistry;

        var taskList = new List<Task>();
        var stateMap = new Dictionary<Task, (StashInstance Handle, TaskRegistry.TaskState State, long Id)>();

        foreach (object? h in handles)
        {
            if (h is not StashInstance taskHandle || taskHandle.TypeName != "TaskHandle")
            {
                throw new RuntimeError("task.awaitAny() expects TaskHandle values.");
            }

            long id = GetTaskId(taskHandle);
            if (!registry.Tasks.TryGetValue(id, out TaskRegistry.TaskState? state))
            {
                throw new RuntimeError("task.awaitAny(): invalid or unknown task handle.");
            }

            taskList.Add(state.DotNetTask);
            stateMap[state.DotNetTask] = (taskHandle, state, id);
        }

        int idx = Task.WaitAny(taskList.ToArray());
        Task completed = taskList[idx];
        var (completedHandle, completedState, completedId) = stateMap[completed];

        // Clean up the completed task
        registry.Tasks.TryRemove(completedId, out _);
        completedState.Cts.Dispose();
        completedHandle.SetField("status", completedState.Status, null);

        // Cancel and clean up all remaining tasks
        foreach (var (task, (handle, state, id)) in stateMap)
        {
            if (task == completed)
            {
                continue;
            }

            try { state.Cts.Cancel(); } catch (ObjectDisposedException) { }
            // Wait briefly for the task to acknowledge cancellation
            try { task.Wait(TimeSpan.FromSeconds(1)); } catch { }
            registry.Tasks.TryRemove(id, out _);
            try { state.Cts.Dispose(); } catch (ObjectDisposedException) { }
        }

        if (completedState.Status.MemberName == "Failed")
        {
            throw new RuntimeError(completedState.Error ?? "Task failed.");
        }

        if (completedState.Status.MemberName == "Cancelled")
        {
            throw new RuntimeError("Task was cancelled.");
        }

        return completedState.Result;
    }

    private static object? Status(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not StashInstance handle || handle.TypeName != "TaskHandle")
        {
            throw new RuntimeError("task.status() expects a TaskHandle.");
        }

        long id = GetTaskId(handle);
        if (!interpreter.TaskRegistry.Tasks.TryGetValue(id, out TaskRegistry.TaskState? state))
        {
            // Task was already awaited and cleaned up; return status from handle
            return handle.GetField("status", null);
        }
        return state.Status;
    }

    private static object? Cancel(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not StashInstance handle || handle.TypeName != "TaskHandle")
        {
            throw new RuntimeError("task.cancel() expects a TaskHandle.");
        }

        long id = GetTaskId(handle);
        if (interpreter.TaskRegistry.Tasks.TryGetValue(id, out TaskRegistry.TaskState? state))
        {
            state.Cts.Cancel();
        }
        return null;
    }

    private static object? All(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 1 || args[0] is not List<object?> items)
        {
            throw new RuntimeError("task.all() expects an array of Futures or TaskHandles.");
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
            else if (item is StashInstance handle && handle.TypeName == "TaskHandle")
            {
                long id = (long)handle.GetField("id", null)!;
                if (!interpreter.TaskRegistry.Tasks.TryGetValue(id, out var state))
                {
                    throw new RuntimeError("task.all(): invalid or unknown task handle.");
                }
                // Wrap the Task in a Task<object?> that extracts the result
                var tState = state;
                tasks.Add(tState.DotNetTask.ContinueWith(t =>
                {
                    if (tState.Status.MemberName == "Failed")
                        return (object?)new StashError(tState.Error ?? "Task failed.", "TaskError");
                    if (tState.Status.MemberName == "Cancelled")
                        return (object?)new StashError("Task was cancelled.", "TaskCancelled");
                    return tState.Result;
                }));
            }
            else
            {
                // Non-future values are treated as already-resolved
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
            throw new RuntimeError("task.race() expects an array of Futures or TaskHandles.");
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
            else if (item is StashInstance handle && handle.TypeName == "TaskHandle")
            {
                long id = (long)handle.GetField("id", null)!;
                if (!interpreter.TaskRegistry.Tasks.TryGetValue(id, out var state))
                {
                    throw new RuntimeError("task.race(): invalid or unknown task handle.");
                }
                var tState = state;
                tasks.Add(tState.DotNetTask.ContinueWith(t =>
                {
                    if (tState.Status.MemberName == "Failed")
                        throw new RuntimeError(tState.Error ?? "Task failed.");
                    if (tState.Status.MemberName == "Cancelled")
                        throw new RuntimeError("Task was cancelled.");
                    return tState.Result;
                }));
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

    private static long GetTaskId(StashInstance handle)
    {
        object? idVal = handle.GetField("id", null);
        if (idVal is long id)
        {
            return id;
        }

        throw new RuntimeError("Invalid task handle: missing id.");
    }
}
