using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>Describes one stage of a streaming pipe chain.</summary>
internal sealed record PipeStage(string Program, List<string> Arguments, byte Flags);

/// <summary>Controls how a pipeline's output is handled.</summary>
internal enum PipelineOutputMode
{
    /// <summary>
    /// Last stage's stdout/stderr are captured and returned as strings.
    /// Used by <c>$(cmd1 | cmd2)</c> command expressions.
    /// </summary>
    Captured,

    /// <summary>
    /// All stages' stdout and stderr inherit the parent terminal (no capture).
    /// Used by bare shell-mode pipeline lines (Phase 4+).
    /// In this mode the return value carries empty strings for stdout and stderr.
    /// </summary>
    Passthrough,
}

/// <summary>
/// External process execution (captured and passthrough modes).
/// </summary>
public sealed partial class VirtualMachine
{
    // ── Public shell-mode entry points (Phase 4) ─────────────────────────────

    /// <summary>
    /// Run a single command in passthrough mode (terminal I/O inherited).
    /// Used by <c>ShellRunner</c> for single-stage bare commands.
    /// </summary>
    /// <returns>The command's exit code.</returns>
    public int RunPassthroughCommand(string program, List<string> args, SourceSpan? span = null)
    {
        var (_, _, exitCode) = ExecPassthrough(program, args, span, _ct);
        return exitCode;
    }

    /// <summary>
    /// Run an N-stage pipeline in passthrough mode (last stage's stdout/stderr inherit the terminal).
    /// Used by <c>ShellRunner</c> for multi-stage bare-command pipelines.
    /// </summary>
    /// <returns>Exit codes for all stages; index [^1] is the overall (last stage) exit code.</returns>
    public int[] RunPassthroughPipeline(
        IReadOnlyList<(string Program, List<string> Args)> stages, SourceSpan? span = null)
    {
        var pipeStages = new List<PipeStage>(stages.Count);
        foreach (var (program, args) in stages)
            pipeStages.Add(new PipeStage(program, args, 0));

        var (_, _, exitCodes) = ExecPipelineStreaming(
            pipeStages, span, _ct, PipelineOutputMode.Passthrough);
        return exitCodes;
    }

    // ────────────────────────────────────────────────────────────────────────

    private static (string Stdout, string Stderr, int ExitCode) ExecCaptured(
        string program, List<string> arguments, string? stdin, SourceSpan? span, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = program,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)
                ?? throw new RuntimeError("Failed to start process.", span);

            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());

            try
            {
                process.WaitForExitAsync(ct).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                try { Task.WaitAll(stdoutTask, stderrTask); } catch { }
                throw;
            }

            Task.WaitAll(stdoutTask, stderrTask);
            return (stdoutTask.Result, stderrTask.Result, process.ExitCode);
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Command execution failed: {ex.Message}", span);
        }
    }

    private static (string Stdout, string Stderr, int ExitCode) ExecPassthrough(
        string program, List<string> arguments, SourceSpan? span, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = program,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                RedirectStandardInput = false,
                UseShellExecute = false,
                CreateNoWindow = false
            };
            foreach (string arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)
                ?? throw new RuntimeError("Failed to start process.", span);

            try
            {
                process.WaitForExitAsync(ct).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            return ("", "", process.ExitCode);
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Command execution failed: {ex.Message}", span);
        }
    }

    private static (string Stdout, string Stderr, int[] ExitCodes) ExecPipelineStreaming(
        List<PipeStage> stages,
        SourceSpan? span,
        CancellationToken ct,
        PipelineOutputMode mode = PipelineOutputMode.Captured)
    {
        int n = stages.Count;
        var processes = new Process[n];
        int started = 0;
        bool captured = mode == PipelineOutputMode.Captured;

        try
        {
            // Phase 1: Start all processes.
            // In Captured mode  : all stages redirect stdout/stderr; no terminal window.
            // In Passthrough mode: last stage lets stdout/stderr inherit the terminal;
            //                     intermediate stages still redirect stdout (feeds the next stage's
            //                     stdin) but let stderr inherit the terminal.
            for (int i = 0; i < n; i++)
            {
                bool isLast = (i == n - 1);
                var stage = stages[i];
                var psi = new ProcessStartInfo
                {
                    FileName               = stage.Program,
                    // Last stage stdout: redirect only when capturing; intermediate stages always
                    // redirect (their stdout feeds the next stage's stdin via a pump task).
                    RedirectStandardOutput = !isLast || captured,
                    // Stderr: redirect only in Captured mode; Passthrough lets all stages write
                    // directly to the terminal's stderr (§7.2).
                    RedirectStandardError  = captured,
                    RedirectStandardInput  = (i > 0),
                    UseShellExecute        = false,
                    CreateNoWindow         = captured,
                };
                foreach (string arg in stage.Arguments)
                    psi.ArgumentList.Add(arg);

                processes[i] = Process.Start(psi)
                    ?? throw new RuntimeError($"Failed to start process: {stage.Program}", span);
                started++;
            }

            // Phase 2: Start stderr drain tasks — Captured mode only.
            // In Passthrough mode stderr is inherited by the terminal so there is nothing to drain.
            // Draining is required in Captured mode to prevent the OS pipe buffer from filling up
            // and deadlocking the pipeline.
            Task<string>[]? stderrTasks = null;
            if (captured)
            {
                stderrTasks = new Task<string>[n];
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    stderrTasks[i] = Task.Run(
                        async () => await processes[idx].StandardError.ReadToEndAsync(ct).ConfigureAwait(false),
                        ct);
                }
            }

            // Phase 3: Start pump tasks (stdout[i] → stdin[i+1]).
            // PumpAsync returns true when the downstream closed its stdin early (broken pipe).
            // On a broken-pipe event we fire ShutdownUpstreamAsync (fire-and-forget) to wait a
            // short grace period for upstream processes to receive SIGPIPE on their next write,
            // then forcibly kill any that are still alive.
            // Note: .NET Process.Kill() sends SIGKILL on POSIX, not SIGTERM.  There is no
            // cross-platform API for SIGTERM via the .NET Process class, so SIGKILL is the
            // practical choice for ensuring termination.
            var pumpTasks = new Task[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                int pumpIdx = i;
                StreamReader from = processes[i].StandardOutput;
                StreamWriter to   = processes[i + 1].StandardInput;
                pumpTasks[i] = Task.Run(
                    async () =>
                    {
                        bool brokePipe = await PumpAsync(from, to, ct).ConfigureAwait(false);
                        if (brokePipe)
                        {
                            // Upstream stages (0..pumpIdx) may not have received SIGPIPE yet.
                            // Give them a 500 ms grace period to exit naturally before killing.
                            _ = ShutdownUpstreamAsync(processes, pumpIdx, gracePeriodMs: 500);
                        }
                    },
                    ct);
            }

            // Phase 4: Collect final stage stdout — Captured mode only.
            Task<string>? stdoutTask = null;
            if (captured)
            {
                stdoutTask = Task.Run(
                    async () => await processes[n - 1].StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false),
                    ct);
            }

            // Phase 5: Wait for all processes to exit.
            var waitTasks = new Task[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                waitTasks[i] = processes[idx].WaitForExitAsync(ct);
            }

            try
            {
                Task.WaitAll(waitTasks, ct);
            }
            catch (OperationCanceledException)
            {
                for (int i = 0; i < started; i++)
                {
                    try { processes[i].Kill(entireProcessTree: true); } catch { }
                }
                var cleanupTasks = new List<Task>(pumpTasks);
                if (stdoutTask != null) cleanupTasks.Add(stdoutTask);
                if (stderrTasks != null) cleanupTasks.AddRange(stderrTasks);
                try { Task.WaitAll(cleanupTasks.ToArray()); } catch { }
                throw;
            }
            catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException))
            {
                for (int i = 0; i < started; i++)
                {
                    try { processes[i].Kill(entireProcessTree: true); } catch { }
                }
                var cleanupTasks = new List<Task>(pumpTasks);
                if (stdoutTask != null) cleanupTasks.Add(stdoutTask);
                if (stderrTasks != null) cleanupTasks.AddRange(stderrTasks);
                try { Task.WaitAll(cleanupTasks.ToArray()); } catch { }
                ct.ThrowIfCancellationRequested();
            }

            // Phase 6: Wait for I/O tasks and collect results.
            try { Task.WaitAll(pumpTasks); } catch { }
            if (stderrTasks != null) Task.WaitAll(stderrTasks);
            stdoutTask?.Wait();

            var exitCodes = new int[n];
            for (int i = 0; i < n; i++)
                exitCodes[i] = processes[i].ExitCode;

            string stdout = stdoutTask?.Result ?? string.Empty;
            string stderr = (stderrTasks != null) ? stderrTasks[n - 1].Result : string.Empty;
            return (stdout, stderr, exitCodes);
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Pipe chain execution failed: {ex.Message}", span);
        }
        finally
        {
            for (int i = 0; i < started; i++)
            {
                try { processes[i].Dispose(); } catch { }
            }
        }
    }

    /// <summary>
    /// Waits <paramref name="gracePeriodMs"/> milliseconds, then kills any upstream pipeline
    /// stages (indices 0..<paramref name="upstreamLastIdx"/> inclusive) that have not yet exited.
    /// Called fire-and-forget when <see cref="PumpAsync"/> detects that a downstream stage closed
    /// its stdin early (broken pipe), giving upstream processes a chance to receive SIGPIPE on
    /// their next write before resorting to a forcible kill.
    /// </summary>
    private static async Task ShutdownUpstreamAsync(Process[] processes, int upstreamLastIdx, int gracePeriodMs)
    {
        await Task.Delay(gracePeriodMs).ConfigureAwait(false);
        for (int i = 0; i <= upstreamLastIdx; i++)
        {
            try
            {
                if (!processes[i].HasExited)
                    processes[i].Kill(entireProcessTree: true);
            }
            catch { /* Process may already be exited or disposed — ignore. */ }
        }
    }

    /// <summary>
    /// Pumps data from <paramref name="from"/> to <paramref name="to"/> using an 8 KB buffer.
    /// Returns <see langword="true"/> when a broken pipe is detected (IOException on write,
    /// meaning the downstream stage closed its stdin early).
    /// Returns <see langword="false"/> on normal EOF or cancellation.
    /// </summary>
    private static async Task<bool> PumpAsync(StreamReader from, StreamWriter to, CancellationToken ct)
    {
        char[] buffer = new char[8192];
        try
        {
            int read;
            while ((read = await from.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                await to.FlushAsync(ct).ConfigureAwait(false);
            }
            try { to.Close(); } catch { }
            return false;
        }
        catch (IOException)
        {
            // Downstream closed its stdin (broken pipe) — signal the caller.
            try { from.Close(); } catch { }
            try { to.Close(); } catch { }
            return true;
        }
        catch (OperationCanceledException)
        {
            try { from.Close(); } catch { }
            try { to.Close(); } catch { }
            return false;
        }
    }
}
