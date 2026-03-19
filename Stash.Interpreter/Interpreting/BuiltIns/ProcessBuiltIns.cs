namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Stash.Interpreting;
using Stash.Interpreting.Exceptions;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'process' namespace built-in functions.
/// </summary>
public static class ProcessBuiltIns
{
    public static void Register(Environment globals)
    {
        // ── process namespace ────────────────────────────────────────────
        var process = new StashNamespace("process");

        // Signal constants
        process.Define("SIGHUP", (long)1);
        process.Define("SIGINT", (long)2);
        process.Define("SIGQUIT", (long)3);
        process.Define("SIGKILL", (long)9);
        process.Define("SIGUSR1", (long)10);
        process.Define("SIGUSR2", (long)12);
        process.Define("SIGTERM", (long)15);

        process.Define("exit", new BuiltInFunction("process.exit", 1, (interp, args) =>
        {
            if (args[0] is not long code)
            {
                throw new RuntimeError("Argument to 'process.exit' must be an integer.");
            }

            interp.CleanupTrackedProcesses();

            if (interp.EmbeddedMode)
            {
                throw new ExitException((int)code);
            }

            System.Environment.Exit((int)code);
            return null;
        }));

        process.Define("exec", new BuiltInFunction("process.exec", 1, (interp, args) =>
        {
            if (interp.EmbeddedMode)
            {
                throw new RuntimeError("'process.exec' is not available in embedded mode.");
            }

            if (args[0] is not string command)
            {
                throw new RuntimeError("Argument to 'process.exec' must be a string.");
            }

            var (program, arguments) = CommandParser.Parse(command);

            // Clean up tracked processes before replacing the process image
            interp.CleanupTrackedProcesses();

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
        }));

        process.Define("spawn", new BuiltInFunction("process.spawn", 1, (interp, args) =>
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
            interp.TrackedProcesses.Add((handle, osProcess));
            return handle;
        }));

        process.Define("wait", new BuiltInFunction("process.wait", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.wait' must be a Process handle.");
            }

            var entry = interp.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null)
            {
                // Already waited — return cached result if available
                if (interp.ProcessWaitCache.TryGetValue(handle, out var cached))
                {
                    return cached;
                }

                return RuntimeValues.CreateCommandResult("", "", -1);
            }

            var osProcess = entry.OsProcess;
            var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
            osProcess.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);

            var result = RuntimeValues.CreateCommandResult(stdoutTask.Result, stderrTask.Result, (long)osProcess.ExitCode);

            // Cache the result so subsequent wait() calls return the same data
            interp.ProcessWaitCache[handle] = result;
            FireExitCallbacks(interp, handle, result);
            return result;
        }));

        process.Define("waitTimeout", new BuiltInFunction("process.waitTimeout", 2, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("First argument to 'process.waitTimeout' must be a Process handle.");
            }

            if (args[1] is not long ms)
            {
                throw new RuntimeError("Second argument to 'process.waitTimeout' must be an integer (milliseconds).");
            }

            var entry = interp.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null)
            {
                return null;
            }

            var osProcess = entry.OsProcess;
            if (!osProcess.WaitForExit((int)ms))
            {
                return null; // timed out
            }

            var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
            Task.WaitAll(stdoutTask, stderrTask);

            var result = RuntimeValues.CreateCommandResult(stdoutTask.Result, stderrTask.Result, (long)osProcess.ExitCode);
            interp.ProcessWaitCache[handle] = result;
            FireExitCallbacks(interp, handle, result);
            return result;
        }));

        process.Define("kill", new BuiltInFunction("process.kill", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.kill' must be a Process handle.");
            }

            var entry = interp.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null || entry.OsProcess.HasExited)
            {
                return false;
            }

            try
            {
                entry.OsProcess.Kill(false); // SIGTERM on Unix, TerminateProcess on Windows
                return true;
            }
            catch
            {
                return false;
            }
        }));

        process.Define("isAlive", new BuiltInFunction("process.isAlive", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.isAlive' must be a Process handle.");
            }

            var entry = interp.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null)
            {
                return false;
            }

            try { return !entry.OsProcess.HasExited; }
            catch { return false; }
        }));

        process.Define("pid", new BuiltInFunction("process.pid", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.pid' must be a Process handle.");
            }

            return handle.GetField("pid", null);
        }));

        process.Define("signal", new BuiltInFunction("process.signal", 2, (interp, args) =>
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

            var entry = interp.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null || entry.OsProcess.HasExited)
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
                        entry.OsProcess.Kill(sig == 9);
                        return true;
                    }

                    // Other signals have no Windows equivalent — terminate as best effort
                    entry.OsProcess.Kill(false);
                    return true;
                }
                else
                {
                    // Unix: use the kill() syscall via P/Invoke for arbitrary signals
                    int result = UnixSignal.Kill(entry.OsProcess.Id, (int)sig);
                    return result == 0;
                }
            }
            catch
            {
                return false;
            }
        }));

        process.Define("detach", new BuiltInFunction("process.detach", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.detach' must be a Process handle.");
            }

            int idx = interp.TrackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
            if (idx >= 0)
            {
                interp.TrackedProcesses.RemoveAt(idx);
                interp.ProcessExitCallbacks.Remove(handle);
                return true;
            }

            return false;
        }));

        process.Define("list", new BuiltInFunction("process.list", 0, (interp, _) =>
        {
            var result = new List<object?>();
            foreach (var (handle, _) in interp.TrackedProcesses)
            {
                result.Add(handle);
            }
            return result;
        }));

        process.Define("read", new BuiltInFunction("process.read", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.read' must be a Process handle.");
            }

            var entry = interp.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null)
            {
                return null;
            }

            try
            {
                var stream = entry.OsProcess.StandardOutput;
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
        }));

        process.Define("write", new BuiltInFunction("process.write", 2, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("First argument to 'process.write' must be a Process handle.");
            }

            if (args[1] is not string data)
            {
                throw new RuntimeError("Second argument to 'process.write' must be a string.");
            }

            var entry = interp.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null || entry.OsProcess.HasExited)
            {
                return false;
            }

            try
            {
                entry.OsProcess.StandardInput.Write(data);
                entry.OsProcess.StandardInput.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }));

        // ── Future Extensions ─────────────────────────────────────────

        process.Define("onExit", new BuiltInFunction("process.onExit", 2, (interp, args) =>
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

            var entry = interp.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null)
            {
                return null;
            }

            if (!interp.ProcessExitCallbacks.TryGetValue(handle, out var callbacks))
            {
                callbacks = new List<IStashCallable>();
                interp.ProcessExitCallbacks[handle] = callbacks;
            }

            callbacks.Add(callback);
            return null;
        }));

        process.Define("daemonize", new BuiltInFunction("process.daemonize", 1, (interp, args) =>
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
        }));

        process.Define("find", new BuiltInFunction("process.find", 1, (interp, args) =>
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
        }));

        process.Define("exists", new BuiltInFunction("process.exists", 1, (interp, args) =>
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
        }));

        process.Define("waitAll", new BuiltInFunction("process.waitAll", 1, (interp, args) =>
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

                var entry = interp.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
                if (entry.OsProcess is null)
                {
                    if (interp.ProcessWaitCache.TryGetValue(handle, out var cached))
                    {
                        results.Add(cached);
                    }
                    else
                    {
                        results.Add(RuntimeValues.CreateCommandResult("", "", -1));
                    }
                    continue;
                }

                var osProcess = entry.OsProcess;
                var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
                var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
                osProcess.WaitForExit();
                Task.WaitAll(stdoutTask, stderrTask);

                var result = RuntimeValues.CreateCommandResult(stdoutTask.Result, stderrTask.Result, (long)osProcess.ExitCode);
                interp.ProcessWaitCache[handle] = result;
                FireExitCallbacks(interp, handle, result);
                results.Add(result);
            }

            return results;
        }));

        process.Define("waitAny", new BuiltInFunction("process.waitAny", 1, (interp, args) =>
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

                var entry = interp.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
                entries.Add((handle, entry.OsProcess));
            }

            // Check if any have already exited
            foreach (var (handle, osProcess) in entries)
            {
                if (osProcess is null)
                {
                    if (interp.ProcessWaitCache.TryGetValue(handle, out var cached))
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
                    interp.ProcessWaitCache[handle] = result;
                    FireExitCallbacks(interp, handle, result);
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
                        interp.ProcessWaitCache[handle] = result;
                        FireExitCallbacks(interp, handle, result);
                        return result;
                    }
                }

                Thread.Sleep(50); // Poll every 50ms
            }
        }));

        globals.Define("process", process);
    }

    /// <summary>
    /// Fires any pending onExit callbacks for a process that has exited.
    /// Must be called from the main thread after obtaining a CommandResult.
    /// </summary>
    internal static void FireExitCallbacks(Interpreter interp, StashInstance handle, StashInstance result)
    {
        if (interp.ProcessExitCallbacks.TryGetValue(handle, out var callbacks))
        {
            interp.ProcessExitCallbacks.Remove(handle);
            foreach (var cb in callbacks)
            {
                try
                {
                    cb.Call(interp, new List<object?> { result });
                }
                catch { /* Errors in onExit callbacks are non-fatal */ }
            }
        }
    }
}
