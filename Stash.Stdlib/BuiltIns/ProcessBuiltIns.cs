namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>process</c> namespace built-in functions for process management.
/// </summary>
/// <remarks>
/// <para>
/// Provides functions for spawning, managing, and communicating with child processes:
/// <c>process.exec</c>, <c>process.spawn</c>, <c>process.wait</c>, <c>process.waitTimeout</c>,
/// <c>process.waitAll</c>, <c>process.waitAny</c>, <c>process.kill</c>, <c>process.signal</c>,
/// <c>process.pid</c>, <c>process.isAlive</c>, <c>process.read</c>, <c>process.write</c>,
/// <c>process.onExit</c>, <c>process.daemonize</c>, <c>process.detach</c>, <c>process.list</c>,
/// <c>process.find</c>, <c>process.exists</c>, <c>process.chdir</c>, <c>process.withDir</c>,
/// and <c>process.exit</c>.
/// </para>
/// <para>
/// Also exposes POSIX signal constants: <c>SIGHUP</c>, <c>SIGINT</c>, <c>SIGQUIT</c>,
/// <c>SIGKILL</c>, <c>SIGUSR1</c>, <c>SIGUSR2</c>, and <c>SIGTERM</c>.
/// This namespace is only registered when the <see cref="StashCapabilities.Process"/>
/// capability is enabled.
/// </para>
/// </remarks>
public static class ProcessBuiltIns
{
    /// <summary>
    /// Registers all <c>process</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Environment"/> to register functions in.</param>
    public static NamespaceDefinition Define()
    {
        // ── process namespace ────────────────────────────────────────────────
        var ns = new NamespaceBuilder("process");
        ns.RequiresCapability(StashCapabilities.Process);

        // Signal constants — POSIX signal numbers for use with process.signal().
        ns.Constant("SIGHUP",  (long)1,  "int", "1");
        ns.Constant("SIGINT",  (long)2,  "int", "2");
        ns.Constant("SIGQUIT", (long)3,  "int", "3");
        ns.Constant("SIGKILL", (long)9,  "int", "9");
        ns.Constant("SIGUSR1", (long)10, "int", "10");
        ns.Constant("SIGUSR2", (long)12, "int", "12");
        ns.Constant("SIGTERM", (long)15, "int", "15");

        // process.exit(code) — Exits the process with the given integer exit code. Runs cleanup for tracked processes first.
        ns.Function("exit", [Param("code", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var code = SvArgs.Long(args, 0, "process.exit");

            ctx.CleanupTrackedProcesses();
            ctx.EmitExit((int)code);
            return StashValue.Null;
        });

        // process.exec(command) — Replaces the current process image with the given command (Unix execvp). On Windows, starts the process and exits with its code.
        ns.Function("exec", [Param("command", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (ctx.EmbeddedMode)
            {
                throw new RuntimeError("'process.exec' is not available in embedded mode.");
            }

            var command = SvArgs.String(args, 0, "process.exec");

            var (program, arguments) = CommandParser.Parse(command);

            // Clean up tracked processes before replacing the process image
            ctx.CleanupTrackedProcesses();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows has no execvp — start the process with inherited I/O and exit
                var psi = new ProcessStartInfo
                {
                    FileName = program,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                };
                foreach (var arg in arguments)
                {
                    psi.ArgumentList.Add(arg);
                }

                try
                {
                    using var child = System.Diagnostics.Process.Start(psi)
                        ?? throw new RuntimeError("Failed to start process.");
                    try
                    {
                        child.WaitForExitAsync(ctx.CancellationToken).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
                    {
                        try { child.Kill(entireProcessTree: true); } catch { }
                        throw;
                    }
                    System.Environment.Exit(child.ExitCode);
                }
                catch (RuntimeError) { throw; }
                catch (System.Exception ex)
                {
                    throw new RuntimeError($"process.exec failed: {ex.Message}");
                }
            }
            else
            {
                // Unix: true exec — replaces the current process image
                int result = UnixSignal.Exec(program, arguments.ToArray());

                // If we get here, execvp failed
                int errno = Marshal.GetLastPInvokeError();
                throw new RuntimeError($"process.exec failed: execvp returned {result} (errno {errno}).");
            }

            return StashValue.Null; // unreachable
        });

        // process.spawn(command) — Spawns a child process with redirected stdio. Returns a Process handle. Use process.wait() to collect output.
        ns.Function("spawn", [Param("command", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var command = SvArgs.String(args, 0, "process.spawn");

            var (program, arguments) = CommandParser.Parse(command);
            var psi = new ProcessStartInfo
            {
                FileName = program,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            var osProcess = System.Diagnostics.Process.Start(psi) ?? throw new RuntimeError("Failed to start process.");
            var fields = new Dictionary<string, StashValue>
            {
                ["pid"] = StashValue.FromInt((long)osProcess.Id),
                ["command"] = StashValue.FromObj(command)
            };
            var handle = new StashInstance("Process", fields);
            ctx.TrackedProcesses.Add((handle, osProcess));
            return StashValue.FromObj(handle);
        }, returnType: "Process");

        // process.wait(handle) — Waits for a spawned process to exit and returns a CommandResult with stdout, stderr, and exitCode.
        ns.Function("wait", [Param("handle", "Process")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var handle = SvArgs.Instance(args, 0, "Process", "process.wait");

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null)
            {
                // Already waited — return cached result if available
                if (ctx.ProcessWaitCache.TryGetValue(handle, out var cached))
                {
                    return StashValue.FromObj(cached);
                }

                return StashValue.FromObj(RuntimeValues.CreateCommandResult("", "", -1));
            }

            var osProcess = entry.Process;
            var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
            try
            {
                osProcess.WaitForExitAsync(ctx.CancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
            {
                try { osProcess.Kill(entireProcessTree: true); } catch { }
                throw;
            }
            Task.WaitAll(new[] { stdoutTask, stderrTask }, ctx.CancellationToken);

            var result = RuntimeValues.CreateCommandResult(stdoutTask.Result, stderrTask.Result, (long)osProcess.ExitCode);

            // Remove from tracking and dispose the OS process handle
            int idx = ctx.TrackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
            if (idx >= 0)
            {
                ctx.TrackedProcesses.RemoveAt(idx);
            }
            try { osProcess.Dispose(); }
            catch { /* Best-effort disposal */ }

            // Cache the result so subsequent wait() calls return the same data
            ctx.ProcessWaitCache[handle] = result;
            FireExitCallbacks(ctx, handle, result);
            return StashValue.FromObj(result);
        }, returnType: "CommandResult");

        // process.waitTimeout(handle, ms) — Waits up to the given milliseconds for a process to exit. Returns a CommandResult or null if timed out.
        ns.Function("waitTimeout", [Param("handle", "Process"), Param("ms", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var handle = SvArgs.Instance(args, 0, "Process", "process.waitTimeout");
            var ms = SvArgs.Long(args, 1, "process.waitTimeout");

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null)
            {
                return StashValue.Null;
            }

            var osProcess = entry.Process;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
                cts.CancelAfter((int)ms);
                osProcess.WaitForExitAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (!ctx.CancellationToken.IsCancellationRequested)
            {
                return StashValue.Null; // our own CancelAfter fired — timed out
            }

            var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
            Task.WaitAll(new[] { stdoutTask, stderrTask }, ctx.CancellationToken);

            var result = RuntimeValues.CreateCommandResult(stdoutTask.Result, stderrTask.Result, (long)osProcess.ExitCode);

            // Remove from tracking and dispose the OS process handle
            int idx = ctx.TrackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
            if (idx >= 0)
            {
                ctx.TrackedProcesses.RemoveAt(idx);
            }
            try { osProcess.Dispose(); }
            catch { /* Best-effort disposal */ }

            ctx.ProcessWaitCache[handle] = result;
            FireExitCallbacks(ctx, handle, result);
            return StashValue.FromObj(result);
        });

        // process.kill(handle) — Sends SIGTERM (Unix) or terminates (Windows) a running process. Returns true on success.
        ns.Function("kill", [Param("handle", "Process")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var handle = SvArgs.Instance(args, 0, "Process", "process.kill");

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null || entry.Process.HasExited)
            {
                return StashValue.FromBool(false);
            }

            try
            {
                entry.Process.Kill(false); // SIGTERM on Unix, TerminateProcess on Windows
                return StashValue.FromBool(true);
            }
            catch
            {
                return StashValue.FromBool(false);
            }
        });

        // process.isAlive(handle) — Returns true if the process is still running, false if it has exited.
        ns.Function("isAlive", [Param("handle", "Process")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var handle = SvArgs.Instance(args, 0, "Process", "process.isAlive");

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null)
            {
                return StashValue.FromBool(false);
            }

            try { return StashValue.FromBool(!entry.Process.HasExited); }
            catch { return StashValue.FromBool(false); }
        });

        // process.pid(handle) — Returns the OS process ID (integer) for a spawned Process handle.
        ns.Function("pid", [Param("handle", "Process")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var handle = SvArgs.Instance(args, 0, "Process", "process.pid");

            return handle.GetField("pid", null);
        });

        // process.signal(handle, signum) — Sends a POSIX signal (integer) to a running process. Use process.SIGTERM etc. as constants. Returns true on success.
        ns.Function("signal", [Param("handle", "Process"), Param("signum", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var handle = SvArgs.Instance(args, 0, "Process", "process.signal");
            var sig = SvArgs.Long(args, 1, "process.signal");

            if (sig < 1 || sig > 64)
            {
                throw new RuntimeError($"Signal number must be between 1 and 64, got {sig}.");
            }

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null || entry.Process.HasExited)
            {
                return StashValue.FromBool(false);
            }

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows: map common signals to Process API
                    if (sig == 9 /* SIGKILL */ || sig == 15 /* SIGTERM */)
                    {
                        entry.Process.Kill(sig == 9);
                        return StashValue.FromBool(true);
                    }

                    // Other signals have no Windows equivalent — terminate as best effort
                    entry.Process.Kill(false);
                    return StashValue.FromBool(true);
                }
                else
                {
                    // Unix: use the kill() syscall via P/Invoke for arbitrary signals
                    int result = UnixSignal.Kill(entry.Process.Id, (int)sig);
                    return StashValue.FromBool(result == 0);
                }
            }
            catch
            {
                return StashValue.FromBool(false);
            }
        });

        // process.detach(handle) — Removes a Process handle from tracking. The process continues running but will not be cleaned up on script exit.
        ns.Function("detach", [Param("handle", "Process")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var handle = SvArgs.Instance(args, 0, "Process", "process.detach");

            int idx = ctx.TrackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
            if (idx >= 0)
            {
                ctx.TrackedProcesses.RemoveAt(idx);
                ctx.ProcessExitCallbacks.Remove(handle);
                return StashValue.FromBool(true);
            }

            return StashValue.FromBool(false);
        });

        // process.list() — Returns an array of all currently tracked Process handles spawned by this script.
        ns.Function("list", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> _args) =>
        {
            var result = new List<StashValue>();
            foreach (var (handle, _) in ctx.TrackedProcesses)
            {
                result.Add(StashValue.FromObj(handle));
            }
            return StashValue.FromObj(result);
        });

        // process.read(handle) — Non-blocking read from a process's stdout. Returns a string chunk or null if no data is available.
        ns.Function("read", [Param("handle", "Process")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var handle = SvArgs.Instance(args, 0, "Process", "process.read");

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null)
            {
                return StashValue.Null;
            }

            try
            {
                var stream = entry.Process.StandardOutput;
                if (stream.Peek() == -1)
                {
                    return StashValue.Null;
                }

                var buffer = new char[4096];
                int read = stream.Read(buffer, 0, buffer.Length);
                return read > 0 ? StashValue.FromObj(new string(buffer, 0, read)) : StashValue.Null;
            }
            catch
            {
                return StashValue.Null;
            }
        });

        // process.write(handle, data) — Writes a string to a process's stdin. Returns true on success, false if the process has exited.
        ns.Function("write", [Param("handle", "Process"), Param("data", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var handle = SvArgs.Instance(args, 0, "Process", "process.write");
            var data = SvArgs.String(args, 1, "process.write");

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null || entry.Process.HasExited)
            {
                return StashValue.FromBool(false);
            }

            try
            {
                entry.Process.StandardInput.Write(data);
                entry.Process.StandardInput.Flush();
                return StashValue.FromBool(true);
            }
            catch
            {
                return StashValue.FromBool(false);
            }
        });

        // ── Future Extensions ─────────────────────────────────────────

        // process.onExit(handle, callback) — Registers a callback function to be called when the process exits. Callback receives a CommandResult.
        ns.Function("onExit", [Param("handle", "Process"), Param("callback", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var handle = SvArgs.Instance(args, 0, "Process", "process.onExit");
            var callback = SvArgs.Callable(args, 1, "process.onExit");

            if (callback.MinArity > 1)
            {
                throw new RuntimeError("Callback for 'process.onExit' must accept at least 1 argument (the CommandResult).");
            }

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null)
            {
                return StashValue.Null;
            }

            if (!ctx.ProcessExitCallbacks.TryGetValue(handle, out var callbacks))
            {
                callbacks = new List<IStashCallable>();
                ctx.ProcessExitCallbacks[handle] = callbacks;
            }

            callbacks.Add(callback);
            return StashValue.Null;
        });

        // process.daemonize(command) — Starts a process fully detached from the script (no stdio redirection). Returns a Process handle; the process is NOT tracked and survives script exit.
        ns.Function("daemonize", [Param("command", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var command = SvArgs.String(args, 0, "process.daemonize");

            var (program, arguments) = CommandParser.Parse(command);
            var psi = new ProcessStartInfo
            {
                FileName = program,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                RedirectStandardInput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            var osProcess = System.Diagnostics.Process.Start(psi) ?? throw new RuntimeError("Failed to daemonize process.");

            var fields = new Dictionary<string, StashValue>
            {
                ["pid"] = StashValue.FromInt((long)osProcess.Id),
                ["command"] = StashValue.FromObj(command)
            };
            var handle = new StashInstance("Process", fields);

            // Daemonized processes are NOT tracked — they survive script exit
            return StashValue.FromObj(handle);
        });

        // process.find(name) — Returns an array of Process handles for all OS processes matching the given name.
        ns.Function("find", [Param("name", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var name = SvArgs.String(args, 0, "process.find");

            var result = new List<StashValue>();
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(name);
                foreach (var p in processes)
                {
                    using (p)
                    {
                        var fields = new Dictionary<string, StashValue>
                        {
                            ["pid"] = StashValue.FromInt((long)p.Id),
                            ["command"] = StashValue.FromObj(name)
                        };
                        result.Add(StashValue.FromObj(new StashInstance("Process", fields)));
                    }
                }
            }
            catch
            {
                // Permission issues or other OS errors — return empty array
            }

            return StashValue.FromObj(result);
        });

        // process.exists(pid) — Returns true if a process with the given OS PID is currently running.
        ns.Function("exists", [Param("pid", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var pid = SvArgs.Long(args, 0, "process.exists");

            try
            {
                var p = System.Diagnostics.Process.GetProcessById((int)pid);
                bool alive = !p.HasExited;
                p.Dispose();
                return StashValue.FromBool(alive);
            }
            catch
            {
                return StashValue.FromBool(false);
            }
        });

        // process.waitAll(handles) — Waits for all processes in the array to exit. Returns an array of CommandResult values in the same order.
        ns.Function("waitAll", [Param("handles", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var procs = SvArgs.StashList(args, 0, "process.waitAll");

            var results = new List<StashValue>();
            foreach (StashValue item in procs)
            {
                if (item.ToObject() is not StashInstance handle || handle.TypeName != "Process")
                {
                    throw new RuntimeError("All elements in 'process.waitAll' array must be Process handles.");
                }

                var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
                if (entry.Process is null)
                {
                    if (ctx.ProcessWaitCache.TryGetValue(handle, out var cached))
                    {
                        results.Add(StashValue.FromObj(cached));
                    }
                    else
                    {
                        results.Add(StashValue.FromObj(RuntimeValues.CreateCommandResult("", "", -1)));
                    }
                    continue;
                }

                var osProcess = entry.Process;
                var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
                var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
                osProcess.WaitForExit();
                Task.WaitAll(stdoutTask, stderrTask);

                var result = RuntimeValues.CreateCommandResult(stdoutTask.Result, stderrTask.Result, (long)osProcess.ExitCode);

                int idx = ctx.TrackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
                if (idx >= 0)
                {
                    ctx.TrackedProcesses.RemoveAt(idx);
                }
                try { osProcess.Dispose(); }
                catch { /* Best-effort disposal */ }

                ctx.ProcessWaitCache[handle] = result;
                FireExitCallbacks(ctx, handle, result);
                results.Add(StashValue.FromObj(result));
            }

            return StashValue.FromObj(results);
        });

        // process.waitAny(handles) — Waits until any process in the array exits. Returns the CommandResult of the first process to finish.
        ns.Function("waitAny", [Param("handles", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var procs = SvArgs.StashList(args, 0, "process.waitAny");

            if (procs.Count == 0)
            {
                throw new RuntimeError("'process.waitAny' requires a non-empty array.");
            }

            // Validate all handles first
            var entries = new List<(StashInstance Handle, System.Diagnostics.Process? OsProcess)>();
            foreach (StashValue item in procs)
            {
                if (item.ToObject() is not StashInstance handle || handle.TypeName != "Process")
                {
                    throw new RuntimeError("All elements in 'process.waitAny' array must be Process handles.");
                }

                var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
                entries.Add((handle, entry.Process));
            }

            // Check if any have already exited
            foreach (var (handle, osProcess) in entries)
            {
                if (osProcess is null)
                {
                    if (ctx.ProcessWaitCache.TryGetValue(handle, out var cached))
                    {
                        return StashValue.FromObj(cached);
                    }
                    return StashValue.FromObj(RuntimeValues.CreateCommandResult("", "", -1));
                }

                if (osProcess.HasExited)
                {
                    var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
                    var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
                    Task.WaitAll(stdoutTask, stderrTask);

                    var result = RuntimeValues.CreateCommandResult(stdoutTask.Result, stderrTask.Result, (long)osProcess.ExitCode);

                    int idx = ctx.TrackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
                    if (idx >= 0)
                    {
                        ctx.TrackedProcesses.RemoveAt(idx);
                    }
                    try { osProcess.Dispose(); }
                    catch { /* Best-effort disposal */ }

                    ctx.ProcessWaitCache[handle] = result;
                    FireExitCallbacks(ctx, handle, result);
                    return StashValue.FromObj(result);
                }
            }

            // Poll until one exits
            while (true)
            {
                foreach (var (handle, osProcess) in entries)
                {
                    if (osProcess is not null && osProcess.HasExited)
                    {
                        var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
                        var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
                        Task.WaitAll(stdoutTask, stderrTask);

                        var result = RuntimeValues.CreateCommandResult(stdoutTask.Result, stderrTask.Result, (long)osProcess.ExitCode);

                        int idx = ctx.TrackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
                        if (idx >= 0)
                        {
                            ctx.TrackedProcesses.RemoveAt(idx);
                        }
                        try { osProcess.Dispose(); }
                        catch { /* Best-effort disposal */ }

                        ctx.ProcessWaitCache[handle] = result;
                        FireExitCallbacks(ctx, handle, result);
                        return StashValue.FromObj(result);
                    }
                }

                Thread.Sleep(50); // Poll every 50ms
            }
        });

        // process.chdir(path) — Changes the current working directory of the script process to the given path.
        ns.Function("chdir", [Param("path", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var path = SvArgs.String(args, 0, "process.chdir");

            string resolved = System.IO.Path.GetFullPath(path);
            if (!System.IO.Directory.Exists(resolved))
            {
                throw new RuntimeError($"process.chdir: directory does not exist: '{resolved}'.");
            }

            System.Environment.CurrentDirectory = resolved;
            return StashValue.Null;
        });

        // process.withDir(path, fn) — Temporarily changes the working directory to path, calls fn(), then restores the original directory. Returns fn's return value.
        ns.Function("withDir", [Param("path", "string"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var path = SvArgs.String(args, 0, "process.withDir");
            var fn = SvArgs.Callable(args, 1, "process.withDir");

            string resolved = System.IO.Path.GetFullPath(path);
            if (!System.IO.Directory.Exists(resolved))
            {
                throw new RuntimeError($"process.withDir: directory does not exist: '{resolved}'.");
            }

            string previous = System.Environment.CurrentDirectory;
            System.Environment.CurrentDirectory = resolved;
            try
            {
                return ctx.InvokeCallbackDirect(fn, ReadOnlySpan<StashValue>.Empty);
            }
            finally
            {
                System.Environment.CurrentDirectory = previous;
            }
        });

        ns.Struct("CommandResult", [
            new BuiltInField("stdout", "string"),
            new BuiltInField("stderr", "string"),
            new BuiltInField("exitCode", "int"),
        ]);
        ns.Struct("Process", [
            new BuiltInField("pid", "int"),
            new BuiltInField("command", "string"),
        ]);

        return ns.Build();
    }

    /// <summary>
    /// Fires any pending onExit callbacks for a process that has exited.
    /// Must be called from the main thread after obtaining a CommandResult.
    /// </summary>
    internal static void FireExitCallbacks(IInterpreterContext ctx, StashInstance handle, StashInstance result)
    {
        if (ctx.ProcessExitCallbacks.TryGetValue(handle, out var callbacks))
        {
            ctx.ProcessExitCallbacks.Remove(handle);
            foreach (var cb in callbacks)
            {
                try
                {
                    ctx.InvokeCallbackDirect(cb, new StashValue[] { StashValue.FromObj(result) });
                }
                catch { /* Errors in onExit callbacks are non-fatal */ }
            }
        }
    }
}
