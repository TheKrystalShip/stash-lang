using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Runtime;

namespace Stash.Cli.Shell;

/// <summary>
/// Orchestrates the full shell-mode pipeline for a single REPL input line:
///   parse → expand args → execute passthrough → update exit code.
///
/// Phase 4 scope:
///   • Single-command and multi-stage pipelines.
///   • Redirects are parsed but NOT executed (throws CommandError if present).
///   • cd/pwd/exit/quit are NOT desugared (Phase 7).
///
/// Errors propagate as <see cref="RuntimeError"/> with error type
/// <see cref="StashErrorTypes.CommandError"/> so the REPL prints them.
/// </summary>
internal sealed class ShellRunner
{
    private readonly ShellContext _ctx;

    public ShellRunner(ShellContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>
    /// Execute a shell-mode line: parse → expand → run passthrough.
    /// Updates <see cref="VirtualMachine.LastExitCode"/> after each execution.
    /// </summary>
    public void Run(string line)
    {
        ShellCommandLine ast = ShellLineLexer.Parse(line);

        // Phase 4: redirects are lexed but not yet implemented.
        if (ast.Redirects.Count > 0)
            throw new RuntimeError(
                "redirects are not yet supported in shell mode (Phase 5+)",
                null, StashErrorTypes.CommandError);

        if (ast.Stages.Count == 1)
            RunSingleStage(ast.Stages[0]);
        else
            RunPipeline(ast.Stages);
    }

    // ── Single-stage execution ───────────────────────────────────────────────

    private void RunSingleStage(ShellStage stage)
    {
        var (program, args) = BuildArgv(stage);

        int exitCode;
        try
        {
            exitCode = _ctx.Vm.RunPassthroughCommand(program, args, span: null);
        }
        catch (RuntimeError ex) when (IsSpawnFailure(ex))
        {
            throw WrapSpawnError(program, ex);
        }

        _ctx.Vm.LastExitCode = exitCode;
    }

    // ── Pipeline execution ───────────────────────────────────────────────────

    private void RunPipeline(IReadOnlyList<ShellStage> stages)
    {
        var resolved = new List<(string Program, List<string> Args)>(stages.Count);
        foreach (var stage in stages)
            resolved.Add(BuildArgv(stage));

        int[] exitCodes;
        try
        {
            exitCodes = _ctx.Vm.RunPassthroughPipeline(resolved, span: null);
        }
        catch (RuntimeError ex) when (IsSpawnFailure(ex))
        {
            // Try to identify the failing stage from the error message.
            throw WrapSpawnError(stages[0].Program, ex);
        }

        _ctx.Vm.LastExitCode = exitCodes[^1];
    }

    // ── Arg building ─────────────────────────────────────────────────────────

    private (string Program, List<string> Args) BuildArgv(ShellStage stage)
    {
        // Tilde-expand the program name if needed (no glob on program).
        string program = ExpandProgramName(stage.Program);

        // Expand the raw args string through the full §6 pipeline.
        List<string> args = ArgExpander.Expand(stage.RawArgs, _ctx.Vm, span: null);

        return (program, args);
    }

    private static string ExpandProgramName(string program)
    {
        if (string.IsNullOrEmpty(program)) return program;

        // Tilde expand.
        if (program == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (program.StartsWith("~/", StringComparison.Ordinal) ||
            program.StartsWith("~\\", StringComparison.Ordinal))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, program[2..]);
        }

        return program;
    }

    // ── Spawn-failure detection and wrapping ─────────────────────────────────

    private static bool IsSpawnFailure(RuntimeError ex)
    {
        // Process.Start failures surface as RuntimeError wrapping either
        // Win32Exception (command not found / permission denied) or
        // FileNotFoundException (on some platforms).
        string msg = ex.Message;
        return msg.Contains("Failed to start process", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Command execution failed", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("No such file", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimeError WrapSpawnError(string program, RuntimeError inner)
    {
        string msg = inner.Message;

        if (msg.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Failed to start", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeError(
                $"command not found: {program}",
                inner.Span, StashErrorTypes.CommandError);
        }

        if (msg.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeError(
                $"permission denied: {program}",
                inner.Span, StashErrorTypes.CommandError);
        }

        return inner;
    }
}
