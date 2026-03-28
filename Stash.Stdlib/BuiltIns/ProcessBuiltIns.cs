namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Stash.Interpreting;
using Stash.Runtime;
using Stash.Runtime.Types;
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
        ns.Function("exit", [Param("code", "int")], (ctx, args) =>
        {
            if (args[0] is not long code)
            {
                throw new RuntimeError("Argument to 'process.exit' must be an integer.");
            }

            ctx.CleanupTrackedProcesses();
            ctx.EmitExit((int)code);
            return null;
        });

        // process.exec(command) — Replaces the current process image with the given command (Unix execvp). On Windows, starts the process and exits with its code.
        ns.Function("exec", [Param("command", "string")], (ctx, args) =>
        {
            if (ctx.EmbeddedMode)
            {
                throw new RuntimeError("'process.exec' is not available in embedded mode.");
            }

            if (args[0] is not string command)
            {
                throw new RuntimeError("Argument to 'process.exec' must be a string.");
            }

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
                    child.WaitForExit();
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

            return null; // unreachable
        });

        // process.spawn(command) — Spawns a child process with redirected stdio. Returns a Process handle. Use process.wait() to collect output.
        ns.Function("spawn", [Param("command", "string")], (ctx, args) =>
        {
            if (args[0] is not string command)
            {
                throw new RuntimeError("Argument to 'process.spawn' must be a string.");
            }

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
            var fields = new Dictionary<string, object?>
            {
                ["pid"] = (long)osProcess.Id,
                ["command"] = command
            };
            var handle = new StashInstance("Process", fields);
            ctx.TrackedProcesses.Add((handle, osProcess));
            return handle;
        }, returnType: "Process");

        // process.wait(handle) — Waits for a spawned process to exit and returns a CommandResult with stdout, stderr, and exitCode.
        ns.Function("wait", [Param("handle", "Process")], (ctx, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.wait' must be a Process handle.");
            }

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null)
            {
                // Already waited — return cached result if available
                if (ctx.ProcessWaitCache.TryGetValue(handle, out var cached))
                {
                    return cached;
                }

                return RuntimeValues.CreateCommandResult("", "", -1);
            }

            var osProcess = entry.Process;
            var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
            osProcess.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);

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
            return result;
        }, returnType: "CommandResult");

        // process.waitTimeout(handle, ms) — Waits up to the given milliseconds for a process to exit. Returns a CommandResult or null if timed out.
        ns.Function("waitTimeout", [Param("handle", "Process"), Param("ms", "int")], (ctx, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("First argument to 'process.waitTimeout' must be a Process handle.");
            }

            if (args[1] is not long ms)
            {
                throw new RuntimeError("Second argument to 'process.waitTimeout' must be an integer (milliseconds).");
            }

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null)
            {
                return null;
            }

            var osProcess = entry.Process;
            if (!osProcess.WaitForExit((int)ms))
            {
                return null; // timed out
            }

            var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
            Task.WaitAll(stdoutTask, stderrTask);

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
            return result;
        });

        // process.kill(handle) — Sends SIGTERM (Unix) or terminates (Windows) a running process. Returns true on success.
        ns.Function("kill", [Param("handle", "Process")], (ctx, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.kill' must be a Process handle.");
            }

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null || entry.Process.HasExited)
            {
                return false;
            }

            try
            {
                entry.Process.Kill(false); // SIGTERM on Unix, TerminateProcess on Windows
                return true;
            }
            catch
            {
                return false;
            }
        });

        // process.isAlive(handle) — Returns true if the process is still running, false if it has exited.
        ns.Function("isAlive", [Param("handle", "Process")], (ctx, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.isAlive' must be a Process handle.");
            }

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null)
            {
                return false;
            }

            try { return !entry.Process.HasExited; }
            catch { return false; }
        });

        // process.pid(handle) — Returns the OS process ID (integer) for a spawned Process handle.
        ns.Function("pid", [Param("handle", "Process")], (ctx, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.pid' must be a Process handle.");
            }

            return handle.GetField("pid", null);
        });

        // process.signal(handle, signum) — Sends a POSIX signal (integer) to a running process. Use process.SIGTERM etc. as constants. Returns true on success.
        ns.Function("signal", [Param("handle", "Process"), Param("signum", "int")], (ctx, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("First argument to 'process.signal' must be a Process handle.");
            }

            if (args[1] is not long sig)
            {
                throw new RuntimeError("Second argument to 'process.signal' must be an integer (signal number).");
            }

            if (sig < 1 || sig > 64)
            {
                throw new RuntimeError($"Signal number must be between 1 and 64, got {sig}.");
            }

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null || entry.Process.HasExited)
            {
                return false;
            }

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows: map common signals to Process API
                    if (sig == 9 /* SIGKILL */ || sig == 15 /* SIGTERM */)
                    {
                        entry.Process.Kill(sig == 9);
                        return true;
                    }

                    // Other signals have no Windows equivalent — terminate as best effort
                    entry.Process.Kill(false);
                    return true;
                }
                else
                {
                    // Unix: use the kill() syscall via P/Invoke for arbitrary signals
                    int result = UnixSignal.Kill(entry.Process.Id, (int)sig);
                    return result == 0;
                }
            }
            catch
            {
                return false;
            }
        });

        // process.detach(handle) — Removes a Process handle from tracking. The process continues running but will not be cleaned up on script exit.
        ns.Function("detach", [Param("handle", "Process")], (ctx, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.detach' must be a Process handle.");
            }

            int idx = ctx.TrackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
            if (idx >= 0)
            {
                ctx.TrackedProcesses.RemoveAt(idx);
                ctx.ProcessExitCallbacks.Remove(handle);
                return true;
            }

            return false;
        });

        // process.list() — Returns an array of all currently tracked Process handles spawned by this script.
        ns.Function("list", [], (ctx, _) =>
        {
            var result = new List<object?>();
            foreach (var (handle, _) in ctx.TrackedProcesses)
            {
                result.Add(handle);
            }
            return result;
        });

        // process.read(handle) — Non-blocking read from a process's stdout. Returns a string chunk or null if no data is available.
        ns.Function("read", [Param("handle", "Process")], (ctx, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.read' must be a Process handle.");
            }

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null)
            {
                return null;
            }

            try
            {
                var stream = entry.Process.StandardOutput;
                if (stream.Peek() == -1)
                {
                    return null;
                }

                var buffer = new char[4096];
                int read = stream.Read(buffer, 0, buffer.Length);
                return read > 0 ? new string(buffer, 0, read) : null;
            }
            catch
            {
                return null;
            }
        });

        // process.write(handle, data) — Writes a string to a process's stdin. Returns true on success, false if the process has exited.
        ns.Function("write", [Param("handle", "Process"), Param("data", "string")], (ctx, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("First argument to 'process.write' must be a Process handle.");
            }

            if (args[1] is not string data)
            {
                throw new RuntimeError("Second argument to 'process.write' must be a string.");
            }

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null || entry.Process.HasExited)
            {
                return false;
            }

            try
            {
                entry.Process.StandardInput.Write(data);
                entry.Process.StandardInput.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        });

        // ── Future Extensions ─────────────────────────────────────────

        // process.onExit(handle, callback) — Registers a callback function to be called when the process exits. Callback receives a CommandResult.
        ns.Function("onExit", [Param("handle", "Process"), Param("callback", "function")], (ctx, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("First argument to 'process.onExit' must be a Process handle.");
            }

            if (args[1] is not IStashCallable callback)
            {
                throw new RuntimeError("Second argument to 'process.onExit' must be a function.");
            }

            if (callback.MinArity > 1)
            {
                throw new RuntimeError("Callback for 'process.onExit' must accept at least 1 argument (the CommandResult).");
            }

            var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.Process is null)
            {
                return null;
            }

            if (!ctx.ProcessExitCallbacks.TryGetValue(handle, out var callbacks))
            {
                callbacks = new List<IStashCallable>();
                ctx.ProcessExitCallbacks[handle] = callbacks;
            }

            callbacks.Add(callback);
            return null;
        });

        // process.daemonize(command) — Starts a process fully detached from the script (no stdio redirection). Returns a Process handle; the process is NOT tracked and survives script exit.
        ns.Function("daemonize", [Param("command", "string")], (ctx, args) =>
        {
            if (args[0] is not string command)
            {
                throw new RuntimeError("Argument to 'process.daemonize' must be a string.");
            }

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

            var fields = new Dictionary<string, object?>
            {
                ["pid"] = (long)osProcess.Id,
                ["command"] = command
            };
            var handle = new StashInstance("Process", fields);

            // Daemonized processes are NOT tracked — they survive script exit
            return handle;
        });

        // process.find(name) — Returns an array of Process handles for all OS processes matching the given name.
        ns.Function("find", [Param("name", "string")], (ctx, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("Argument to 'process.find' must be a string.");
            }

            var result = new List<object?>();
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(name);
                foreach (var p in processes)
                {
                    using (p)
                    {
                        var fields = new Dictionary<string, object?>
                        {
                            ["pid"] = (long)p.Id,
                            ["command"] = name
                        };
                        result.Add(new StashInstance("Process", fields));
                    }
                }
            }
            catch
            {
                // Permission issues or other OS errors — return empty array
            }

            return result;
        });

        // process.exists(pid) — Returns true if a process with the given OS PID is currently running.
        ns.Function("exists", [Param("pid", "int")], (ctx, args) =>
        {
            if (args[0] is not long pid)
            {
                throw new RuntimeError("Argument to 'process.exists' must be an integer (PID).");
            }

            try
            {
                var p = System.Diagnostics.Process.GetProcessById((int)pid);
                bool alive = !p.HasExited;
                p.Dispose();
                return alive;
            }
            catch
            {
                return false;
            }
        });

        // process.waitAll(handles) — Waits for all processes in the array to exit. Returns an array of CommandResult values in the same order.
        ns.Function("waitAll", [Param("handles", "array")], (ctx, args) =>
        {
            if (args[0] is not List<object?> procs)
            {
                throw new RuntimeError("Argument to 'process.waitAll' must be an array of Process handles.");
            }

            var results = new List<object?>();
            foreach (var item in procs)
            {
                if (item is not StashInstance handle || handle.TypeName != "Process")
                {
                    throw new RuntimeError("All elements in 'process.waitAll' array must be Process handles.");
                }

                var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
                if (entry.Process is null)
                {
                    if (ctx.ProcessWaitCache.TryGetValue(handle, out var cached))
                    {
                        results.Add(cached);
                    }
                    else
                    {
                        results.Add(RuntimeValues.CreateCommandResult("", "", -1));
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
                results.Add(result);
            }

            return results;
        });

        // process.waitAny(handles) — Waits until any process in the array exits. Returns the CommandResult of the first process to finish.
        ns.Function("waitAny", [Param("handles", "array")], (ctx, args) =>
        {
            if (args[0] is not List<object?> procs)
            {
                throw new RuntimeError("Argument to 'process.waitAny' must be an array of Process handles.");
            }

            if (procs.Count == 0)
            {
                throw new RuntimeError("'process.waitAny' requires a non-empty array.");
            }

            // Validate all handles first
            var entries = new List<(StashInstance Handle, System.Diagnostics.Process? OsProcess)>();
            foreach (var item in procs)
            {
                if (item is not StashInstance handle || handle.TypeName != "Process")
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
                        return cached;
                    }
                    return RuntimeValues.CreateCommandResult("", "", -1);
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
                    return result;
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
                        return result;
                    }
                }

                Thread.Sleep(50); // Poll every 50ms
            }
        });

        // process.chdir(path) — Changes the current working directory of the script process to the given path.
        ns.Function("chdir", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'process.chdir' must be a string.");
            }

            string resolved = System.IO.Path.GetFullPath(path);
            if (!System.IO.Directory.Exists(resolved))
            {
                throw new RuntimeError($"process.chdir: directory does not exist: '{resolved}'.");
            }

            System.Environment.CurrentDirectory = resolved;
            return null;
        });

        // process.withDir(path, fn) — Temporarily changes the working directory to path, calls fn(), then restores the original directory. Returns fn's return value.
        ns.Function("withDir", [Param("path", "string"), Param("fn", "function")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("First argument to 'process.withDir' must be a string (directory path).");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'process.withDir' must be a function.");
            }

            string resolved = System.IO.Path.GetFullPath(path);
            if (!System.IO.Directory.Exists(resolved))
            {
                throw new RuntimeError($"process.withDir: directory does not exist: '{resolved}'.");
            }

            string previous = System.Environment.CurrentDirectory;
            System.Environment.CurrentDirectory = resolved;
            try
            {
                return fn.Call(ctx, new List<object?>());
            }
            finally
            {
                System.Environment.CurrentDirectory = previous;
            }
        });

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
                    cb.Call(ctx, new List<object?> { result });
                }
                catch { /* Errors in onExit callbacks are non-fatal */ }
            }
        }
    }
}
