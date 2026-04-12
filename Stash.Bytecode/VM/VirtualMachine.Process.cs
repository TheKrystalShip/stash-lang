using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Bytecode;

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
}
