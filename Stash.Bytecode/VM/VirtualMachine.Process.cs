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

/// <summary>
/// External process execution (captured and passthrough modes).
/// </summary>
public sealed partial class VirtualMachine
{
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
        CancellationToken ct)
    {
        int n = stages.Count;
        var processes = new Process[n];
        int started = 0;

        try
        {
            // Phase 1: Start all processes concurrently
            for (int i = 0; i < n; i++)
            {
                var stage = stages[i];
                var psi = new ProcessStartInfo
                {
                    FileName               = stage.Program,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    RedirectStandardInput  = (i > 0),
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                foreach (string arg in stage.Arguments)
                    psi.ArgumentList.Add(arg);

                processes[i] = Process.Start(psi)
                    ?? throw new RuntimeError($"Failed to start process: {stage.Program}", span);
                started++;
            }

            // Phase 2: Start stderr drain tasks for ALL stages (prevents OS buffer deadlock)
            var stderrTasks = new Task<string>[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                stderrTasks[i] = Task.Run(
                    async () => await processes[idx].StandardError.ReadToEndAsync(ct).ConfigureAwait(false),
                    ct);
            }

            // Phase 3: Start pump tasks (stdout[i] → stdin[i+1])
            var pumpTasks = new Task[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                StreamReader from = processes[i].StandardOutput;
                StreamWriter to   = processes[i + 1].StandardInput;
                pumpTasks[i] = Task.Run(
                    async () => await PumpAsync(from, to, ct).ConfigureAwait(false),
                    ct);
            }

            // Phase 4: Collect final stage stdout
            var stdoutTask = Task.Run(
                async () => await processes[n - 1].StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false),
                ct);

            // Phase 5: Wait for all processes to exit
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
                var cleanupTasks = new List<Task>(pumpTasks) { stdoutTask };
                cleanupTasks.AddRange(stderrTasks);
                try { Task.WaitAll(cleanupTasks.ToArray()); } catch { }
                throw;
            }
            catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException))
            {
                for (int i = 0; i < started; i++)
                {
                    try { processes[i].Kill(entireProcessTree: true); } catch { }
                }
                var cleanupTasks = new List<Task>(pumpTasks) { stdoutTask };
                cleanupTasks.AddRange(stderrTasks);
                try { Task.WaitAll(cleanupTasks.ToArray()); } catch { }
                ct.ThrowIfCancellationRequested();
            }

            // Phase 6: Wait for pump tasks and collect results
            try { Task.WaitAll(pumpTasks); } catch { }
            Task.WaitAll(stderrTasks);
            stdoutTask.Wait();

            var exitCodes = new int[n];
            for (int i = 0; i < n; i++)
                exitCodes[i] = processes[i].ExitCode;

            return (stdoutTask.Result, stderrTasks[n - 1].Result, exitCodes);
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

    private static async Task PumpAsync(StreamReader from, StreamWriter to, CancellationToken ct)
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
        }
        catch (IOException)
        {
            try { from.Close(); } catch { }
            try { to.Close(); } catch { }
        }
        catch (OperationCanceledException)
        {
            try { from.Close(); } catch { }
            try { to.Close(); } catch { }
        }
    }
}
