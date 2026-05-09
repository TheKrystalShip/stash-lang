namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;

/// <summary>
/// Registers the <c>process</c> namespace built-in functions for process management.
/// </summary>
[StashNamespace(Capability = StashCapabilities.Process)]
public static partial class ProcessBuiltIns
{
    /// <summary>Set by Stash.Cli on REPL startup. Returns a snapshot of the current in-memory history. Null means persistence disabled.</summary>
    public static Func<IReadOnlyList<string>>? HistoryListProvider;

    /// <summary>Set by Stash.Cli on REPL startup. Clears in-memory + on-disk history. Null means persistence disabled.</summary>
    public static Action? HistoryClearHandler;

    /// <summary>Set by Stash.Cli on REPL startup. Appends an entry to in-memory + on-disk history (subject to dedup and leading-space rules). Null means persistence disabled.</summary>
    public static Action<string>? HistoryAddHandler;

    // ── Signal constants (deprecated — use Signal.X instead) ─────────────────

    /// <summary>POSIX SIGHUP signal number (1). Deprecated — use Signal.Hup.</summary>
    [StashConst, StashDeprecated("Signal.Hup")]
    public const long SIGHUP = 1L;

    /// <summary>POSIX SIGINT signal number (2). Deprecated — use Signal.Int.</summary>
    [StashConst, StashDeprecated("Signal.Int")]
    public const long SIGINT = 2L;

    /// <summary>POSIX SIGQUIT signal number (3). Deprecated — use Signal.Quit.</summary>
    [StashConst, StashDeprecated("Signal.Quit")]
    public const long SIGQUIT = 3L;

    /// <summary>POSIX SIGKILL signal number (9). Deprecated — use Signal.Kill.</summary>
    [StashConst, StashDeprecated("Signal.Kill")]
    public const long SIGKILL = 9L;

    /// <summary>POSIX SIGUSR1 signal number (10). Deprecated — use Signal.Usr1.</summary>
    [StashConst, StashDeprecated("Signal.Usr1")]
    public const long SIGUSR1 = 10L;

    /// <summary>POSIX SIGUSR2 signal number (12). Deprecated — use Signal.Usr2.</summary>
    [StashConst, StashDeprecated("Signal.Usr2")]
    public const long SIGUSR2 = 12L;

    /// <summary>POSIX SIGTERM signal number (15). Deprecated — use Signal.Term.</summary>
    [StashConst, StashDeprecated("Signal.Term")]
    public const long SIGTERM = 15L;

    // ── Struct declarations ───────────────────────────────────────────────────

    /// <summary>Result of a process execution.</summary>
    [StashStruct]
    public sealed record CommandResult(string Stdout, string Stderr, long ExitCode);

    /// <summary>A handle to a spawned child process.</summary>
    [StashStruct(Name = "Process")]
    public sealed record ProcessInfo(long Pid, string Command);

    // ── Functions ─────────────────────────────────────────────────────────────

    /// <summary>Exits the current process with the given integer exit code (default 0). Runs all pending defer blocks before terminating. Cannot be caught by try/catch.</summary>
    /// <param name="code">(optional) The exit code. Defaults to 0</param>
    /// <returns>Does not return — exits the process</returns>
    [StashFn, StashDeprecated("env.exit")]
    private static void Exit(IInterpreterContext ctx, params StashValue[] rest)
    {
        long code = 0L;
        if (rest.Length > 0)
        {
            var v = rest[0];
            if (!v.IsInt)
                throw new RuntimeError("First argument to 'process.exit' must be an integer.", errorType: StashErrorTypes.TypeError);
            code = v.AsInt;
        }
        GlobalBuiltIns.EmitExitImpl(ctx, code);
    }

    /// <summary>Replaces the current process image with the given command (Unix execvp / Windows spawn-and-exit). Does not return on success.</summary>
    /// <param name="command">The command and arguments to execute</param>
    /// <returns>Does not return — replaces the process</returns>
    [StashFn]
    private static void Replace(IInterpreterContext ctx, string command)
    {
        if (ctx.EmbeddedMode)
        {
            throw new RuntimeError("'process.replace' is not available in embedded mode.", errorType: StashErrorTypes.NotSupportedError);
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
                    ?? throw new RuntimeError("Failed to start process.", errorType: StashErrorTypes.IOError);
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
                throw new RuntimeError($"process.replace failed: {ex.Message}", errorType: StashErrorTypes.IOError);
            }
        }
        else
        {
            // Unix: true exec — replaces the current process image
            int result = UnixSignal.Exec(program, arguments.ToArray());

            // If we get here, execvp failed
            int errno = Marshal.GetLastPInvokeError();
            throw new RuntimeError($"process.replace failed: execvp returned {result} (errno {errno}).", errorType: StashErrorTypes.IOError);
        }
    }

    /// <summary>Runs a program with an explicit argv array. Unlike `$(…)`, no shell tokenisation or glob expansion is applied to the args — they are passed verbatim.</summary>
    /// <param name="program">The executable name or path</param>
    /// <param name="args">Array of argument strings</param>
    /// <param name="opts">Optional ExecOptions controlling mode, strict, redirect, cwd, env</param>
    /// <returns>A CommandResult (stdout, stderr, exitCode) in Capture/Passthrough mode, or a StreamingProcess in Stream mode</returns>
    // Raw = true: arg0 is polymorphic — may be a string (normal call) or a List<StashValue> (array
    // interpolation from the compiler, e.g. $(${cmd}) where cmd=["ls","-la"]). The typed generator
    // cannot express this split, so we keep Raw and inspect the raw StashValue directly.
    [StashFn(Raw = true)]
    private static StashValue Exec(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        // Phase B: program may be an array when the first argv slot is an array-typed
        // interpolation (e.g. $(${cmd}) where cmd=["ls","-la"]). In that case the first
        // element is the program name and the rest are prepended to the explicit args.
        string program;
        List<string> extraLeading = new();

        var arg0 = args[0];
        if (arg0.IsObj && arg0.AsObj is List<StashValue> programArray)
        {
            if (programArray.Count == 0)
                throw new RuntimeError("process.exec: program array cannot be empty.", errorType: StashErrorTypes.ValueError);
            program = StringifyArg(programArray[0]);
            for (int pi = 1; pi < programArray.Count; pi++)
                extraLeading.Add(StringifyArg(programArray[pi]));
        }
        else
        {
            program = SvArgs.String(args, 0, "process.exec");
        }

        if (string.IsNullOrEmpty(program))
            throw new RuntimeError("process.exec: 'program' must be a non-empty string.", errorType: StashErrorTypes.ValueError);

        if (args.Length < 2 || args[1].IsNull || !(args[1].IsObj && args[1].AsObj is List<StashValue>))
            throw new RuntimeError("process.exec: 'args' must be an array.", errorType: StashErrorTypes.TypeError);

        var rawArgs = (List<StashValue>)args[1].AsObj!;
        var argv = ResolveArgv(rawArgs, "process.exec");

        if (extraLeading.Count > 0)
        {
            extraLeading.AddRange(argv);
            argv = extraLeading;
        }

        var opts = args.Length >= 3 ? ParseExecOptions(args[2], "process.exec") : ExecOptionsData.Default;

        string label = argv.Count > 0
            ? $"{program} {string.Join(" ", argv)}"
            : program;

        return ExecuteExec(ctx, program, argv, opts, label);
    }

    /// <summary>Runs a multi-stage pipeline from an array of PipelineStage values. Each stage specifies program and args explicitly — no shell tokenisation applied.</summary>
    /// <param name="stages">Array of PipelineStage values (each with program and args fields)</param>
    /// <param name="opts">Optional ExecOptions controlling mode, strict, redirect</param>
    /// <returns>A CommandResult in Capture mode, or a StreamingProcess in Stream mode</returns>
    // Raw = true: the second arg is optional and has a complex struct/dict shape (ExecOptions) that
    // the typed generator cannot handle without deeper infrastructure. Keeping Raw preserves the
    // existing ParseExecOptions path for both the typed and untyped callers.
    [StashFn(Raw = true)]
    private static StashValue Pipeline(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1 || args[0].IsNull || !(args[0].IsObj && args[0].AsObj is List<StashValue>))
            throw new RuntimeError("process.pipeline: 'stages' must be a non-empty array.", errorType: StashErrorTypes.TypeError);

        var rawStages = (List<StashValue>)args[0].AsObj!;
        if (rawStages.Count == 0)
            throw new RuntimeError("process.pipeline: 'stages' must contain at least one stage.", errorType: StashErrorTypes.ValueError);

        var opts = args.Length >= 2 ? ParseExecOptions(args[1], "process.pipeline") : ExecOptionsData.Default;

        if (opts.Mode == ExecModeEnum.Passthrough)
            throw new RuntimeError("process.pipeline: Passthrough mode is not allowed in pipelines. Use Capture or Stream.", errorType: StashErrorTypes.ValueError);

        // Parse each stage.
        var stages = new List<(string Program, List<string> Argv)>(rawStages.Count);
        var commandBuf = new System.Text.StringBuilder();
        for (int i = 0; i < rawStages.Count; i++)
        {
            var (stageProgram, stageArgv) = ParsePipelineStage(rawStages[i], i, "process.pipeline");
            stages.Add((stageProgram, stageArgv));
            if (commandBuf.Length > 0) commandBuf.Append(" | ");
            commandBuf.Append(stageProgram);
            foreach (string a in stageArgv) { commandBuf.Append(' '); commandBuf.Append(a); }
        }
        string commandLabel = commandBuf.ToString();

        return ExecutePipeline(ctx, stages, opts, commandLabel);
    }

    /// <summary>Spawns a child process with redirected stdio. Returns a Process handle. Use process.wait() to collect output and the exit code.</summary>
    /// <param name="command">The command and arguments to spawn</param>
    /// <returns>A Process handle</returns>
    [StashFn(ReturnType = "Process")]
    private static StashValue Spawn(IInterpreterContext ctx, string command)
    {

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

        var osProcess = System.Diagnostics.Process.Start(psi) ?? throw new RuntimeError("Failed to start process.", errorType: StashErrorTypes.IOError);
        var fields = new Dictionary<string, StashValue>
        {
            ["pid"] = StashValue.FromInt((long)osProcess.Id),
            ["command"] = StashValue.FromObj(command)
        };
        var handle = new StashInstance("Process", fields);
        ctx.TrackedProcesses.Add((handle, osProcess));
        return StashValue.FromObj(handle);
    }

    /// <summary>Waits for a spawned process to exit and returns a CommandResult with stdout, stderr, and exitCode.</summary>
    /// <param name="handle">The Process handle returned by process.spawn()</param>
    /// <returns>A CommandResult with stdout, stderr, and exitCode</returns>
    [StashFn(ReturnType = "CommandResult")]
    private static StashValue Wait(IInterpreterContext ctx, StashValue handleVal)
    {
        var handle = ExtractProcessHandle(handleVal, "process.wait");

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
    }

    /// <summary>Waits up to the given number of milliseconds for a process to exit. Returns a CommandResult on success, or null if the timeout expires.</summary>
    /// <param name="handle">The Process handle returned by process.spawn()</param>
    /// <param name="ms">Maximum wait time in milliseconds</param>
    /// <returns>A CommandResult, or null if timed out</returns>
    [StashFn]
    private static StashValue WaitTimeout(IInterpreterContext ctx, StashValue handleVal, long ms)
    {
        var handle = ExtractProcessHandle(handleVal, "process.waitTimeout");

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
    }

    /// <summary>Sends SIGTERM (Unix) or terminates (Windows) a running process. Returns true on success, false if the process has already exited.</summary>
    /// <param name="handle">The Process handle returned by process.spawn()</param>
    /// <returns>True if the signal was sent, false if the process was not running</returns>
    [StashFn]
    private static StashValue Kill(IInterpreterContext ctx, StashValue handleVal)
    {
        var handle = ExtractProcessHandle(handleVal, "process.kill");

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
    }

    /// <summary>Returns true if the process is still running, false if it has exited.</summary>
    /// <param name="handle">The Process handle returned by process.spawn()</param>
    /// <returns>True if the process is running</returns>
    [StashFn]
    private static StashValue IsAlive(IInterpreterContext ctx, StashValue handleVal)
    {
        var handle = ExtractProcessHandle(handleVal, "process.isAlive");

        var entry = ctx.TrackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
        if (entry.Process is null)
        {
            return StashValue.FromBool(false);
        }

        try { return StashValue.FromBool(!entry.Process.HasExited); }
        catch { return StashValue.FromBool(false); }
    }

    /// <summary>Returns the OS process ID for a spawned Process handle.</summary>
    /// <param name="handle">The Process handle returned by process.spawn()</param>
    /// <returns>The integer process ID</returns>
    [StashFn]
    private static StashValue Pid(IInterpreterContext ctx, StashValue handleVal)
    {
        var handle = ExtractProcessHandle(handleVal, "process.pid");
        return handle.GetField("pid", null);
    }

    /// <summary>Sends a POSIX signal to a running process. Use process.SIGTERM, process.SIGKILL, etc. as signal number constants. Returns true on success.</summary>
    /// <param name="handle">The Process handle returned by process.spawn()</param>
    /// <param name="signum">The POSIX signal number (1–64)</param>
    /// <returns>True if the signal was sent successfully</returns>
    [StashFn]
    private static StashValue Signal(IInterpreterContext ctx, StashValue handleVal, StashValue sigArg)
    {
        var handle = ExtractProcessHandle(handleVal, "process.signal");

        long sig;
        if (sigArg.IsObj && sigArg.AsObj is StashEnumValue ev && ev.TypeName == "Signal")
        {
            if (!GlobalBuiltIns.SignalNumbers.TryGetValue(ev.MemberName, out sig))
            {
                throw new RuntimeError($"Unknown Signal member '{ev.MemberName}'.", errorType: StashErrorTypes.ValueError);
            }
        }
        else
        {
            if (!sigArg.IsInt)
                throw new RuntimeError("Second argument to 'process.signal' must be an integer or a Signal enum value.", errorType: StashErrorTypes.TypeError);
            sig = sigArg.AsInt;
        }

        if (sig < 1 || sig > 64)
        {
            throw new RuntimeError($"Signal number must be between 1 and 64, got {sig}.", errorType: StashErrorTypes.ValueError);
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
    }

    /// <summary>Removes a Process handle from tracking. The process continues running but will not be cleaned up on script exit. Returns true if the handle was tracked.</summary>
    /// <param name="handle">The Process handle to detach</param>
    /// <returns>True if the handle was found and removed</returns>
    [StashFn]
    private static StashValue Detach(IInterpreterContext ctx, StashValue handleVal)
    {
        var handle = ExtractProcessHandle(handleVal, "process.detach");

        int idx = ctx.TrackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
        if (idx >= 0)
        {
            ctx.TrackedProcesses.RemoveAt(idx);
            ctx.ProcessExitCallbacks.Remove(handle);
            return StashValue.FromBool(true);
        }

        return StashValue.FromBool(false);
    }

    /// <summary>Returns an array of all Process handles currently tracked by this script.</summary>
    /// <returns>An array of Process handles</returns>
    [StashFn]
    private static List<StashValue> List(IInterpreterContext ctx)
    {
        var result = new List<StashValue>();
        foreach (var (handle, _) in ctx.TrackedProcesses)
        {
            result.Add(StashValue.FromObj(handle));
        }
        return result;
    }

    /// <summary>Non-blocking read from a process's stdout. Returns a string chunk if data is available, or null if no data is ready.</summary>
    /// <param name="handle">The Process handle returned by process.spawn()</param>
    /// <returns>A string chunk, or null if no data is available</returns>
    [StashFn]
    private static StashValue Read(IInterpreterContext ctx, StashValue handleVal)
    {
        var handle = ExtractProcessHandle(handleVal, "process.read");

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
    }

    /// <summary>Writes a string to a process's stdin. Returns true on success, false if the process has already exited.</summary>
    /// <param name="handle">The Process handle returned by process.spawn()</param>
    /// <param name="data">The string data to write to stdin</param>
    /// <returns>True if written successfully</returns>
    [StashFn]
    private static StashValue Write(IInterpreterContext ctx, StashValue handleVal, string data)
    {
        var handle = ExtractProcessHandle(handleVal, "process.write");

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
    }

    /// <summary>Registers a callback function to be called when the process exits. The callback receives a CommandResult as its argument.</summary>
    /// <param name="handle">The Process handle returned by process.spawn()</param>
    /// <param name="callback">A function that accepts a CommandResult</param>
    /// <returns>null</returns>
    [StashFn]
    private static StashValue OnExit(IInterpreterContext ctx, StashValue handleVal, IStashCallable callback)
    {
        var handle = ExtractProcessHandle(handleVal, "process.onExit");

        if (callback.MinArity > 1)
        {
            throw new RuntimeError("Callback for 'process.onExit' must accept at least 1 argument (the CommandResult).", errorType: StashErrorTypes.TypeError);
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
    }

    /// <summary>Starts a process fully detached from the script with no stdio redirection. The process is not tracked and survives script exit.</summary>
    /// <param name="command">The command and arguments to daemonize</param>
    /// <returns>A Process handle for the detached process</returns>
    [StashFn]
    private static StashValue Daemonize(IInterpreterContext ctx, string command)
    {

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

        var osProcess = System.Diagnostics.Process.Start(psi) ?? throw new RuntimeError("Failed to daemonize process.", errorType: StashErrorTypes.IOError);

        var fields = new Dictionary<string, StashValue>
        {
            ["pid"] = StashValue.FromInt((long)osProcess.Id),
            ["command"] = StashValue.FromObj(command)
        };
        var handle = new StashInstance("Process", fields);

        // Daemonized processes are NOT tracked — they survive script exit
        return StashValue.FromObj(handle);
    }

    /// <summary>Returns an array of Process handles for all OS processes matching the given name.</summary>
    /// <param name="name">The process name to search for</param>
    /// <returns>An array of matching Process handles</returns>
    [StashFn]
    private static StashValue Find(IInterpreterContext ctx, string name)
    {

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
    }

    /// <summary>Returns true if an OS process with the given PID is currently running.</summary>
    /// <param name="pid">The OS process ID to check</param>
    /// <returns>True if the process exists and is running</returns>
    [StashFn]
    private static bool Exists(IInterpreterContext ctx, long pid)
    {
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
    }

    /// <summary>Waits for all processes in the array to exit. Returns an array of CommandResult values in the same order as the input.</summary>
    /// <param name="handles">An array of Process handles</param>
    /// <returns>An array of CommandResult values</returns>
    [StashFn]
    private static StashValue WaitAll(IInterpreterContext ctx, List<StashValue> procs)
    {

        var results = new List<StashValue>();
        foreach (StashValue item in procs)
        {
            if (item.ToObject() is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("All elements in 'process.waitAll' array must be Process handles.", errorType: StashErrorTypes.TypeError);
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
    }

    /// <summary>Waits until any process in the array exits. Returns the CommandResult of the first process to finish.</summary>
    /// <param name="handles">A non-empty array of Process handles</param>
    /// <returns>The CommandResult of the first process to exit</returns>
    [StashFn]
    private static StashValue WaitAny(IInterpreterContext ctx, List<StashValue> procs)
    {

        if (procs.Count == 0)
        {
            throw new RuntimeError("'process.waitAny' requires a non-empty array.", errorType: StashErrorTypes.ValueError);
        }

        // Validate all handles first
        var entries = new List<(StashInstance Handle, System.Diagnostics.Process? OsProcess)>();
        foreach (StashValue item in procs)
        {
            if (item.ToObject() is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("All elements in 'process.waitAny' array must be Process handles.", errorType: StashErrorTypes.TypeError);
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
    }

    /// <summary>Changes the current working directory to the given path and pushes it onto the directory stack.</summary>
    /// <param name="path">The directory path to change to</param>
    /// <returns>null</returns>
    [StashFn, StashDeprecated("env.chdir")]
    private static void Chdir(IInterpreterContext ctx, string path)
    {
        string expanded = ctx.ExpandTilde(path);
        string resolved = System.IO.Path.GetFullPath(expanded);
        if (!System.IO.Directory.Exists(resolved))
            throw new RuntimeError($"no such directory: {resolved}", errorType: StashErrorTypes.CommandError);

        var stack = ctx.DirStack;
        if (stack.Count >= 256)
            stack.RemoveAt(0);

        System.Environment.CurrentDirectory = resolved;
        stack.Add(resolved);
    }

    /// <summary>Pops the top directory from the stack, changes cwd back to the new top, and returns the popped path. Throws CommandError if the stack is at its root entry.</summary>
    /// <returns>The directory path that was popped</returns>
    [StashFn, StashDeprecated("env.popDir")]
    private static string PopDir(IInterpreterContext ctx)
    {
        var stack = ctx.DirStack;
        if (stack.Count <= 1)
            throw new RuntimeError("directory stack is at root", errorType: StashErrorTypes.CommandError);

        string popped = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        System.Environment.CurrentDirectory = stack[^1];
        return popped;
    }

    /// <summary>Returns a copy of the directory stack, oldest entry first.</summary>
    /// <returns>An array of directory path strings</returns>
    [StashFn, StashDeprecated("env.dirStack")]
    private static List<StashValue> DirStack(IInterpreterContext ctx)
    {
        var stack = ctx.DirStack;
        var result = new List<StashValue>(stack.Count);
        foreach (string dir in stack)
            result.Add(StashValue.FromObj(dir));
        return result;
    }

    /// <summary>Returns the number of entries in the directory stack.</summary>
    /// <returns>The depth as an integer</returns>
    [StashFn, StashDeprecated("env.dirStackDepth")]
    private static long DirStackDepth(IInterpreterContext ctx)
        => (long)ctx.DirStack.Count;

    /// <summary>Temporarily changes the working directory to the given path, calls fn(), then restores the original directory. Returns fn's return value.</summary>
    /// <param name="path">The directory to temporarily change to</param>
    /// <param name="fn">The function to execute in the new directory</param>
    /// <returns>The return value of fn</returns>
    [StashFn, StashDeprecated("env.withDir")]
    private static StashValue WithDir(IInterpreterContext ctx, string path, IStashCallable fn)
    {
        string resolved = System.IO.Path.GetFullPath(path);
        if (!System.IO.Directory.Exists(resolved))
            throw new RuntimeError($"process.withDir: directory does not exist: '{resolved}'.", errorType: StashErrorTypes.IOError);

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
    }

    /// <summary>Returns the exit code of the most recently executed bare command pipeline. Defaults to 0 until any command has run.</summary>
    /// <returns>The exit code as an integer</returns>
    [StashFn, StashDeprecated("shell.lastExitCode")]
    private static long LastExitCode(IInterpreterContext ctx)
        => (long)ctx.GetLastExitCode();

    /// <summary>Returns the REPL command history as an array of strings, oldest-first. Each entry is one logical command line (multi-line entries contain embedded newlines). Returns an empty array when persistence is disabled or in non-interactive script mode.</summary>
    /// <returns>Array of history entries</returns>
    [StashFn]
    private static List<StashValue> HistoryList(IInterpreterContext ctx)
    {
        var snap = HistoryListProvider?.Invoke();
        var list = new List<StashValue>(snap?.Count ?? 0);
        if (snap != null)
        {
            foreach (var s in snap) list.Add(StashValue.FromObj(s));
        }
        return list;
    }

    /// <summary>Clears the REPL command history both in memory and on disk (preserving the file header). No-op when persistence is disabled.</summary>
    /// <returns>null</returns>
    [StashFn]
    private static void HistoryClear(IInterpreterContext ctx)
    {
        HistoryClearHandler?.Invoke();
    }

    /// <summary>Adds the given line to the REPL command history. Subject to the same dedup and leading-space-skip rules as user input. Throws ValueError if line is empty or whitespace-only. No-op when persistence is disabled.</summary>
    /// <param name="line">The command line to append</param>
    /// <returns>null</returns>
    [StashFn]
    private static void HistoryAdd(IInterpreterContext ctx, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            throw new RuntimeError("process.historyAdd: line must not be empty or whitespace-only.", errorType: StashErrorTypes.ValueError);
        HistoryAddHandler?.Invoke(line);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Extracts a Process StashInstance from a StashValue, throwing TypeError if it is not one.
    /// Used by typed built-ins that declare the handle parameter as StashValue to preserve
    /// the StashInstance validation that SvArgs.Instance(args, idx, "Process", funcName) performed
    /// in the old Raw form.
    /// </summary>
    private static StashInstance ExtractProcessHandle(StashValue v, string funcName)
    {
        if (v.IsObj && v.AsObj is StashInstance inst && inst.TypeName == "Process")
            return inst;
        throw new RuntimeError($"First argument to '{funcName}' must be a Process.", errorType: StashErrorTypes.TypeError);
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

    // ── process.exec / process.pipeline helpers ──────────────────────────────

    /// <summary>Execution mode decoded from ExecOptions.</summary>
    private enum ExecModeEnum { Capture, Passthrough, Stream }

    /// <summary>Decoded ExecOptions bag.</summary>
    private sealed record ExecOptionsData(
        ExecModeEnum Mode,
        bool Strict,
        List<RedirectData>? Redirects,
        string? Cwd,
        Dictionary<string, string>? Env)
    {
        public static readonly ExecOptionsData Default = new(ExecModeEnum.Capture, false, null, null, null);
    }

    /// <summary>Decoded RedirectSpec bag.</summary>
    private sealed record RedirectData(string Stream, string Target, bool Append);

    /// <summary>
    /// Resolve a raw StashValue args list into a flat <see cref="List{String}"/> of argv entries.
    /// Handles:
    /// <list type="bullet">
    ///   <item>null → TypeError</item>
    ///   <item><see cref="StashLiteralArg"/> → tilde-expand, pass verbatim (glob expansion requires Phase B wiring)</item>
    ///   <item>array → recursively flatten one level into argv entries</item>
    ///   <item>scalar → stringify</item>
    /// </list>
    /// </summary>
    private static List<string> ResolveArgv(List<StashValue> rawArgs, string callerName)
    {
        var argv = new List<string>(rawArgs.Count);
        foreach (var arg in rawArgs)
            ResolveArgvElement(arg, argv, callerName, depth: 0);
        return argv;
    }

    private static void ResolveArgvElement(StashValue arg, List<string> argv, string callerName, int depth)
    {
        if (arg.IsNull)
            throw new RuntimeError($"{callerName}: argv element cannot be null.", errorType: StashErrorTypes.TypeError);

        object? obj = arg.IsObj ? arg.AsObj : null;

        if (obj is Stash.Runtime.Types.StashLiteralArg litArg)
        {
            // Tilde expansion on start of unquoted tokens.
            string text = litArg.ShouldExpand ? ApplyTilde(litArg.Text) : litArg.Text;
            // Glob expansion is wired in Phase B via ShellExpansion.GlobExpandHandler.
            // For Phase A: fallback to literal text (no glob matching from API layer).
            if (litArg.ShouldExpand && Stash.Runtime.ShellExpansion.GlobExpandHandler is { } globHandler)
            {
                var matches = globHandler(text, System.Environment.CurrentDirectory);
                if (matches.Count == 0)
                    argv.Add(text); // no match — keep literal (same as bash nullglob off)
                else
                    argv.AddRange(matches);
            }
            else
            {
                argv.Add(text);
            }
            return;
        }

        if (obj is List<StashValue> nested && depth == 0)
        {
            // One level of array splat.
            foreach (var elem in nested)
                ResolveArgvElement(elem, argv, callerName, depth: 1);
            return;
        }

        // Scalar: stringify.
        argv.Add(StringifyArg(arg));
    }

    private static string StringifyArg(StashValue v)
    {
        if (v.IsNull) return "";
        if (v.IsBool) return v.AsBool ? "true" : "false";
        if (v.IsInt) return v.AsInt.ToString();
        if (v.IsFloat) return v.AsFloat.ToString("G");
        if (v.IsByte) return v.AsByte.ToString();
        object? obj = v.AsObj;
        return obj switch
        {
            string s => s,
            IVMStringifiable str => str.VMToString(),
            _ => obj?.ToString() ?? ""
        };
    }

    private static void ApplyCwdAndEnv(ProcessStartInfo psi, string? cwd, Dictionary<string, string>? env)
    {
        if (cwd is not null)
            psi.WorkingDirectory = cwd;

        if (env is not null)
        {
            // Replace, do not merge — caller is expected to dict.merge(env.all(), ...) if they want inheritance.
            psi.Environment.Clear();
            foreach (var kvp in env)
                psi.Environment[kvp.Key] = kvp.Value;
        }
    }

    private static string ApplyTilde(string s)
    {
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (s == "~") return home;
        if (s.StartsWith("~/", StringComparison.Ordinal) || s.StartsWith("~\\", StringComparison.Ordinal))
            return System.IO.Path.Combine(home, s[2..]);
        return s;
    }

    /// <summary>Parse a raw <see cref="StashValue"/> into an <see cref="ExecOptionsData"/>.</summary>
    private static ExecOptionsData ParseExecOptions(StashValue raw, string callerName)
    {
        if (raw.IsNull) return ExecOptionsData.Default;

        if (!raw.IsObj)
            throw new RuntimeError($"{callerName}: 'opts' must be an ExecOptions struct or dict, got {raw.Tag}.", errorType: StashErrorTypes.TypeError);

        object? obj = raw.AsObj;

        ExecModeEnum mode = ExecModeEnum.Capture;
        bool strict = false;
        List<RedirectData>? redirects = null;
        string? cwd = null;
        Dictionary<string, string>? env = null;

        if (obj is StashInstance inst)
        {
            // Typed ExecOptions struct
            if (inst.VMTryGetField("mode", out StashValue modeField, null) && !modeField.IsNull)
                mode = ParseExecMode(modeField, callerName);

            if (inst.VMTryGetField("strict", out StashValue strictField, null) && strictField.IsBool)
                strict = strictField.AsBool;

            if (inst.VMTryGetField("redirect", out StashValue redirectField, null) && !redirectField.IsNull)
                redirects = ParseRedirectList(redirectField, callerName);

            if (inst.VMTryGetField("cwd", out StashValue cwdField, null) && cwdField.IsObj && cwdField.AsObj is string cwdStr)
                cwd = cwdStr;

            if (inst.VMTryGetField("env", out StashValue envField, null) && !envField.IsNull)
                env = ParseEnvDict(envField, callerName);
        }
        else if (obj is StashDictionary dict)
        {
            // Untyped dict — flexible fallback (also used by the lowered compiler output)
            if (dict.Has("mode"))
            {
                var modeVal = dict.Get("mode");
                if (!modeVal.IsNull) mode = ParseExecMode(modeVal, callerName);
            }
            if (dict.Has("strict"))
            {
                var strictVal = dict.Get("strict");
                if (strictVal.IsBool) strict = strictVal.AsBool;
            }
            if (dict.Has("redirect"))
            {
                var redirectVal = dict.Get("redirect");
                if (!redirectVal.IsNull) redirects = ParseRedirectList(redirectVal, callerName);
            }
            if (dict.Has("cwd"))
            {
                var cwdVal = dict.Get("cwd");
                if (cwdVal.IsObj && cwdVal.AsObj is string cwdStr) cwd = cwdStr;
            }
            if (dict.Has("env"))
            {
                var envVal = dict.Get("env");
                if (!envVal.IsNull) env = ParseEnvDict(envVal, callerName);
            }
        }
        else
        {
            throw new RuntimeError($"{callerName}: 'opts' must be an ExecOptions struct or dict.", errorType: StashErrorTypes.TypeError);
        }

        return new ExecOptionsData(mode, strict, redirects, cwd, env);
    }

    /// <summary>
    /// Parses a redirect field value that may be either a single RedirectSpec dict/struct or
    /// an array of RedirectSpec dicts/structs (produced by multi-redirect lowering in Phase B).
    /// </summary>
    private static List<RedirectData> ParseRedirectList(StashValue val, string callerName)
    {
        if (val.IsObj && val.AsObj is List<StashValue> arr)
        {
            var list = new List<RedirectData>(arr.Count);
            foreach (var item in arr)
                list.Add(ParseRedirectSpec(item, callerName));
            return list;
        }
        // Single redirect.
        return new List<RedirectData>(1) { ParseRedirectSpec(val, callerName) };
    }

    private static ExecModeEnum ParseExecMode(StashValue val, string callerName)
    {
        if (val.IsObj)
        {
            object? obj = val.AsObj;
            if (obj is StashEnumValue ev && ev.TypeName == "ExecMode")
            {
                return ev.MemberName switch
                {
                    "Capture"     => ExecModeEnum.Capture,
                    "Passthrough" => ExecModeEnum.Passthrough,
                    "Stream"      => ExecModeEnum.Stream,
                    _ => throw new RuntimeError($"{callerName}: Unknown ExecMode member '{ev.MemberName}'.", errorType: StashErrorTypes.ValueError)
                };
            }
            if (obj is string modeStr)
            {
                return modeStr switch
                {
                    "Capture"     => ExecModeEnum.Capture,
                    "Passthrough" => ExecModeEnum.Passthrough,
                    "Stream"      => ExecModeEnum.Stream,
                    _ => throw new RuntimeError($"{callerName}: Unknown ExecMode string '{modeStr}'. Expected 'Capture', 'Passthrough', or 'Stream'.", errorType: StashErrorTypes.ValueError)
                };
            }
        }
        throw new RuntimeError($"{callerName}: 'mode' must be an ExecMode enum value or string.", errorType: StashErrorTypes.TypeError);
    }

    private static RedirectData ParseRedirectSpec(StashValue val, string callerName)
    {
        string stream = "stdout";
        string target = "";
        bool append = false;

        if (!val.IsObj)
            throw new RuntimeError($"{callerName}: 'redirect' must be a RedirectSpec struct or dict.", errorType: StashErrorTypes.TypeError);

        object? obj = val.AsObj;
        if (obj is StashInstance inst)
        {
            if (inst.VMTryGetField("stream", out StashValue sv2, null) && sv2.IsObj && sv2.AsObj is string s)
                stream = s;

            if (inst.VMTryGetField("target", out StashValue tv, null) && tv.IsObj && tv.AsObj is string t)
                target = t;

            if (inst.VMTryGetField("append", out StashValue av, null) && av.IsBool)
                append = av.AsBool;
        }
        else if (obj is StashDictionary dict)
        {
            if (dict.Has("stream")) { var v = dict.Get("stream"); if (v.IsObj && v.AsObj is string s) stream = s; }
            if (dict.Has("target")) { var v = dict.Get("target"); if (v.IsObj && v.AsObj is string t) target = t; }
            if (dict.Has("append")) { var v = dict.Get("append"); if (v.IsBool) append = v.AsBool; }
        }
        else
        {
            throw new RuntimeError($"{callerName}: 'redirect' must be a RedirectSpec struct or dict.", errorType: StashErrorTypes.TypeError);
        }

        if (string.IsNullOrEmpty(target))
            throw new RuntimeError($"{callerName}: RedirectSpec 'target' must be a non-empty string.", errorType: StashErrorTypes.ValueError);

        if (stream is not ("stdout" or "stderr" or "all"))
            throw new RuntimeError($"{callerName}: RedirectSpec 'stream' must be \"stdout\", \"stderr\", or \"all\".", errorType: StashErrorTypes.ValueError);

        return new RedirectData(stream, target, append);
    }

    private static Dictionary<string, string>? ParseEnvDict(StashValue val, string callerName)
    {
        if (!val.IsObj || val.AsObj is not StashDictionary dict)
            throw new RuntimeError($"{callerName}: 'env' must be a dict.", errorType: StashErrorTypes.TypeError);

        var result = new Dictionary<string, string>();
        foreach (var kvp in dict.GetAllEntries())
            result[kvp.Key.ToString()!] = StringifyArg(kvp.Value);
        return result;
    }

    private static (string Program, List<string> Argv) ParsePipelineStage(StashValue raw, int index, string callerName)
    {
        string program = "";
        List<StashValue> rawArgs = new();

        if (raw.IsObj)
        {
            object? obj = raw.AsObj;
            if (obj is StashInstance inst)
            {
                if (inst.VMTryGetField("program", out StashValue pf, null) && pf.IsObj && pf.AsObj is string p)
                    program = p;

                if (inst.VMTryGetField("args", out StashValue af, null) && af.IsObj && af.AsObj is List<StashValue> a)
                    rawArgs = a;
            }
            else if (obj is StashDictionary dict)
            {
                if (dict.Has("program")) { var v = dict.Get("program"); if (v.IsObj && v.AsObj is string p) program = p; }
                if (dict.Has("args")) { var v = dict.Get("args"); if (v.IsObj && v.AsObj is List<StashValue> a) rawArgs = a; }
            }
        }

        if (string.IsNullOrEmpty(program))
            throw new RuntimeError($"{callerName}: stage[{index}] 'program' must be a non-empty string.", errorType: StashErrorTypes.ValueError);

        return (program, ResolveArgv(rawArgs, callerName));
    }

    /// <summary>Core dispatcher for process.exec — applies opts and chooses the execution path.</summary>
    private static StashValue ExecuteExec(
        IInterpreterContext ctx, string program, List<string> argv, ExecOptionsData opts, string commandLabel)
    {
        if (opts.Mode == ExecModeEnum.Stream && opts.Redirects is not null)
            throw new RuntimeError(
                "process.exec: 'redirect' is not supported with Stream mode.",
                errorType: StashErrorTypes.ValueError);

        switch (opts.Mode)
        {
            case ExecModeEnum.Stream:
            {
                var handle = SpawnStreaming(program, argv, commandLabel, opts.Strict, ctx, opts.Cwd, opts.Env);
                return StashValue.FromObj(handle);
            }
            case ExecModeEnum.Passthrough:
            {
                var (_, _, exitCode) = ExecPassthroughDirect(program, argv, ctx.CancellationToken, opts.Cwd, opts.Env);
                if (opts.Strict && exitCode != 0)
                    ThrowCommandError(commandLabel, exitCode, "", "");
                return StashValue.FromObj(RuntimeValues.CreateCommandResult("", "", exitCode));
            }
            default: // Capture
            {
                var (stdout, stderr, exitCode) = ExecCapturedDirect(program, argv, ctx.CancellationToken, opts.Cwd, opts.Env);
                if (opts.Strict && exitCode != 0)
                    ThrowCommandError(commandLabel, exitCode, stderr, stdout);

                // Apply each redirect in order.
                if (opts.Redirects is { } redirs)
                    foreach (var redir in redirs)
                        ApplyRedirect(stdout, stderr, redir);

                return StashValue.FromObj(RuntimeValues.CreateCommandResult(stdout, stderr, exitCode));
            }
        }
    }

    /// <summary>Core dispatcher for process.pipeline.</summary>
    private static StashValue ExecutePipeline(
        IInterpreterContext ctx,
        List<(string Program, List<string> Argv)> stages,
        ExecOptionsData opts,
        string commandLabel)
    {
        if (opts.Mode == ExecModeEnum.Stream)
        {
            var handle = SpawnPipelineStreaming(stages, commandLabel, opts.Strict, ctx, opts.Cwd, opts.Env);
            return StashValue.FromObj(handle);
        }

        // Capture mode (Passthrough rejected by caller)
        var (stdout, stderr, exitCode) = ExecPipelineCaptured(stages, ctx.CancellationToken, opts.Cwd, opts.Env);
        if (opts.Strict && exitCode != 0)
            ThrowCommandError(commandLabel, exitCode, stderr, stdout);

        if (opts.Redirects is { } redirs2)
            foreach (var redir in redirs2)
                ApplyRedirect(stdout, stderr, redir);

        return StashValue.FromObj(RuntimeValues.CreateCommandResult(stdout, stderr, exitCode));
    }

    private static (string Stdout, string Stderr, long ExitCode) ExecCapturedDirect(
        string program, List<string> arguments, CancellationToken ct,
        string? cwd = null, Dictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = program,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in arguments)
            psi.ArgumentList.Add(arg);
        ApplyCwdAndEnv(psi, cwd, env);

        System.Diagnostics.Process process;
        try
        {
            process = System.Diagnostics.Process.Start(psi)
                ?? throw new RuntimeError($"process.exec: failed to start '{program}'.", errorType: StashErrorTypes.CommandError);
        }
        catch (RuntimeError) { throw; }
        catch (System.Exception ex)
        {
            throw new RuntimeError($"process.exec: failed to start '{program}': {ex.Message}", errorType: StashErrorTypes.CommandError);
        }

        using (process)
        {
            var stdoutTask = System.Threading.Tasks.Task.Run(() => process.StandardOutput.ReadToEnd());
            var stderrTask = System.Threading.Tasks.Task.Run(() => process.StandardError.ReadToEnd());

            try
            {
                process.WaitForExitAsync(ct).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                try { System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask); } catch { }
                throw;
            }

            System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask);
            return (stdoutTask.Result, stderrTask.Result, (long)process.ExitCode);
        }
    }

    private static (string Stdout, string Stderr, long ExitCode) ExecPassthroughDirect(
        string program, List<string> arguments, CancellationToken ct,
        string? cwd = null, Dictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = program,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        foreach (string arg in arguments)
            psi.ArgumentList.Add(arg);
        ApplyCwdAndEnv(psi, cwd, env);

        System.Diagnostics.Process process;
        try
        {
            process = System.Diagnostics.Process.Start(psi)
                ?? throw new RuntimeError($"process.exec: failed to start '{program}' in passthrough mode.", errorType: StashErrorTypes.CommandError);
        }
        catch (RuntimeError) { throw; }
        catch (System.Exception ex)
        {
            throw new RuntimeError($"process.exec: failed to start '{program}': {ex.Message}", errorType: StashErrorTypes.CommandError);
        }

        using (process)
        {
            try
            {
                process.WaitForExitAsync(ct).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
            return ("", "", (long)process.ExitCode);
        }
    }

    private static Stash.Runtime.Types.StashStreamingProcess SpawnStreaming(
        string program, List<string> arguments, string commandLabel, bool isStrict, IInterpreterContext ctx,
        string? cwd = null, Dictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = program,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in arguments)
            psi.ArgumentList.Add(arg);
        ApplyCwdAndEnv(psi, cwd, env);

        System.Diagnostics.Process process;
        try
        {
            process = System.Diagnostics.Process.Start(psi)
                ?? throw new RuntimeError($"process.exec: failed to start '{program}' in stream mode.", errorType: StashErrorTypes.CommandError);
        }
        catch (RuntimeError) { throw; }
        catch (System.Exception ex)
        {
            throw new RuntimeError($"process.exec: failed to start '{program}' in stream mode: {ex.Message}", errorType: StashErrorTypes.CommandError);
        }

        // Re-read the token from the context on each iteration tick so that timeout-block
        // cancellation (which mutates the context's CT) propagates into streaming MoveNext.
        return new Stash.Runtime.Types.StashStreamingProcess(process, commandLabel, isStrict, null, () => ctx.CancellationToken);
    }

    private static (string Stdout, string Stderr, long ExitCode) ExecPipelineCaptured(
        List<(string Program, List<string> Argv)> stages, CancellationToken ct,
        string? cwd = null, Dictionary<string, string>? env = null)
    {
        int n = stages.Count;
        var processes = new System.Diagnostics.Process[n];
        int started = 0;

        try
        {
            for (int i = 0; i < n; i++)
            {
                bool isLast = (i == n - 1);
                var (prog, argv) = stages[i];
                var psi = new ProcessStartInfo
                {
                    FileName = prog,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = (i > 0),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (string arg in argv)
                    psi.ArgumentList.Add(arg);
                ApplyCwdAndEnv(psi, cwd, env);

                try
                {
                    processes[i] = System.Diagnostics.Process.Start(psi)
                        ?? throw new RuntimeError($"pipeline stage {i} ('{prog}') failed to start.", errorType: StashErrorTypes.CommandError);
                }
                catch (RuntimeError) { throw; }
                catch (System.Exception ex)
                {
                    throw new RuntimeError($"pipeline stage {i} ('{prog}') failed to start: {ex.Message}", errorType: StashErrorTypes.CommandError);
                }
                started++;
            }

            // Drain stderr for all stages in parallel.
            var stderrTasks = new System.Threading.Tasks.Task<string>[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                stderrTasks[i] = System.Threading.Tasks.Task.Run(
                    async () => await processes[idx].StandardError.ReadToEndAsync(ct).ConfigureAwait(false), ct);
            }

            // Pump stdout[i] → stdin[i+1] for intermediate stages.
            // On broken pipe (downstream exited early), close the upstream read end so the
            // upstream process gets SIGPIPE and terminates promptly instead of hanging.
            var pumpTasks = new System.Threading.Tasks.Task[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                var from = processes[i].StandardOutput;
                var to = processes[i + 1].StandardInput;
                int pumpIdx = i;
                pumpTasks[i] = System.Threading.Tasks.Task.Run(async () =>
                {
                    char[] buf = new char[8192];
                    int read;
                    try
                    {
                        while ((read = await from.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
                        {
                            await to.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                            await to.FlushAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch (IOException)
                    {
                        // Downstream closed its stdin (broken pipe). Close the read end of the
                        // upstream pipe so the upstream process receives SIGPIPE and terminates.
                        try { from.Close(); } catch { }
                    }
                    catch (OperationCanceledException) { }
                    finally { try { to.Close(); } catch { } }
                }, ct);
            }

            // Capture last stage stdout.
            var stdoutTask = System.Threading.Tasks.Task.Run(
                async () => await processes[n - 1].StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false), ct);

            // Wait for all processes.
            var waitTasks = new System.Threading.Tasks.Task[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                waitTasks[i] = processes[idx].WaitForExitAsync(ct);
            }

            try
            {
                System.Threading.Tasks.Task.WaitAll(waitTasks, ct);
            }
            catch (OperationCanceledException)
            {
                for (int i = 0; i < started; i++)
                    try { processes[i].Kill(entireProcessTree: true); } catch { }
                try { System.Threading.Tasks.Task.WaitAll(pumpTasks); } catch { }
                try { System.Threading.Tasks.Task.WaitAll(stderrTasks); } catch { }
                try { stdoutTask.Wait(); } catch { }
                throw;
            }

            try { System.Threading.Tasks.Task.WaitAll(pumpTasks); } catch { }
            System.Threading.Tasks.Task.WaitAll(stderrTasks);
            stdoutTask.Wait();

            string stdout = stdoutTask.Result;
            string stderr = stderrTasks[n - 1].Result;
            long exitCode = (long)processes[n - 1].ExitCode;
            return (stdout, stderr, exitCode);
        }
        catch (RuntimeError) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (System.Exception ex)
        {
            throw new RuntimeError($"process.pipeline: execution failed: {ex.Message}", errorType: StashErrorTypes.CommandError);
        }
        finally
        {
            for (int i = 0; i < started; i++)
                try { processes[i].Dispose(); } catch { }
        }
    }

    private static Stash.Runtime.Types.StashStreamingProcess SpawnPipelineStreaming(
        List<(string Program, List<string> Argv)> stages,
        string commandLabel,
        bool isStrict,
        IInterpreterContext ctx,
        string? cwd = null,
        Dictionary<string, string>? env = null)
    {
        // Token captured for intra-stage pump tasks (fire-and-forget); these can use the
        // initial token because they only need an outer-cancellation observation. Streaming
        // MoveNext re-reads via ctx.CancellationToken so timeout-block updates propagate.
        CancellationToken ct = ctx.CancellationToken;
        int n = stages.Count;
        var processes = new System.Diagnostics.Process[n];
        int started = 0;

        try
        {
            for (int i = 0; i < n; i++)
            {
                var (prog, argv) = stages[i];
                var psi = new ProcessStartInfo
                {
                    FileName = prog,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = (i > 0),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (string arg in argv)
                    psi.ArgumentList.Add(arg);
                ApplyCwdAndEnv(psi, cwd, env);

                try
                {
                    processes[i] = System.Diagnostics.Process.Start(psi)
                        ?? throw new RuntimeError($"pipeline stage {i} ('{prog}') failed to start.", errorType: StashErrorTypes.CommandError);
                }
                catch (RuntimeError) { throw; }
                catch (System.Exception ex)
                {
                    throw new RuntimeError($"pipeline stage {i} ('{prog}') failed to start: {ex.Message}", errorType: StashErrorTypes.CommandError);
                }
                started++;
            }

            // Pump intermediate stages: stdout[i] → stdin[i+1], fire-and-forget.
            for (int i = 0; i < n - 1; i++)
            {
                var from = processes[i].StandardOutput;
                var to = processes[i + 1].StandardInput;
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    char[] buf = new char[8192];
                    int read;
                    try
                    {
                        while ((read = await from.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
                        {
                            await to.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                            await to.FlushAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch (IOException) { }
                    catch (OperationCanceledException) { }
                    finally { try { to.Close(); } catch { } }
                }, ct);
            }

            // StashStreamingProcess takes ownership of all stages.
            return new Stash.Runtime.Types.StashStreamingProcess(processes, commandLabel, isStrict, null, () => ctx.CancellationToken);
        }
        catch (RuntimeError)
        {
            for (int i = 0; i < started; i++)
            {
                try { if (!processes[i].HasExited) processes[i].Kill(entireProcessTree: true); } catch { }
                try { processes[i].Dispose(); } catch { }
            }
            throw;
        }
        catch (System.Exception ex)
        {
            for (int i = 0; i < started; i++)
            {
                try { if (!processes[i].HasExited) processes[i].Kill(entireProcessTree: true); } catch { }
                try { processes[i].Dispose(); } catch { }
            }
            throw new RuntimeError($"process.pipeline: failed to start pipeline: {ex.Message}", errorType: StashErrorTypes.CommandError);
        }
    }

    private static void ApplyRedirect(string stdout, string stderr, RedirectData redir)
    {
        string content = redir.Stream switch
        {
            "stderr" => stderr,
            "all"    => stdout + stderr,
            _        => stdout  // "stdout"
        };
        try
        {
            if (redir.Append)
                System.IO.File.AppendAllText(redir.Target, content);
            else
                System.IO.File.WriteAllText(redir.Target, content);
        }
        catch (System.Exception ex)
        {
            throw new RuntimeError($"process.exec: redirect to '{redir.Target}' failed: {ex.Message}", errorType: StashErrorTypes.IOError);
        }
    }

    private static void ThrowCommandError(string command, long exitCode, string stderr, string stdout)
    {
        throw new RuntimeError(
            $"Command failed with exit code {exitCode}: {command}",
            errorType: StashErrorTypes.CommandError)
        {
            Properties = new Dictionary<string, object?>
            {
                ["exitCode"] = exitCode,
                ["stderr"]   = stderr,
                ["stdout"]   = stdout,
                ["command"]  = command,
            }
        };
    }
}
