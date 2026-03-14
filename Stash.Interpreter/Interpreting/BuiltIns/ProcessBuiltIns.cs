namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

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
            System.Environment.Exit((int)code);
            return null;
        }));

        process.Define("spawn", new BuiltInFunction("process.spawn", 1, (interp, args) =>
        {
            if (args[0] is not string command)
            {
                throw new RuntimeError("Argument to 'process.spawn' must be a string.");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);

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

            return RuntimeValues.CreateCommandResult(stdoutTask.Result, stderrTask.Result, (long)osProcess.ExitCode);
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
                entry.OsProcess.Kill(false); // SIGKILL on Linux
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
                // Use kill command for arbitrary signals since .NET only supports SIGTERM/SIGKILL directly
                var killPsi = new ProcessStartInfo
                {
                    FileName = "/bin/kill",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                killPsi.ArgumentList.Add($"-{sig}");
                killPsi.ArgumentList.Add(entry.OsProcess.Id.ToString());

                using var killProc = System.Diagnostics.Process.Start(killPsi);
                killProc?.WaitForExit();
                return killProc?.ExitCode == 0;
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

        globals.Define("process", process);
    }
}
