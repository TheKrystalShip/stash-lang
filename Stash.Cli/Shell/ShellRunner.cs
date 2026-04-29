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
/// Phase 5 additions:
///   • <c>\cmd</c>  forced shell execution (prefix stripped by lexer).
///   • <c>!cmd</c>  strict mode — non-zero exit raises <see cref="StashErrorTypes.CommandError"/>.
///   • Redirects (<c>&gt;</c> <c>&gt;&gt;</c> <c>2&gt;</c> <c>2&gt;&gt;</c> <c>&amp;&gt;</c> <c>&amp;&gt;&gt;</c>)
///     applied to the last pipeline stage.
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
    /// Throws <see cref="RuntimeError"/> (<see cref="StashErrorTypes.CommandError"/>) in strict mode
    /// when any stage exits non-zero.
    /// </summary>
    public void Run(string line)
    {
        ShellCommandLine ast = ShellLineLexer.Parse(line);

        int[] exitCodes;

        if (ast.Redirects.Count > 0)
            exitCodes = RunWithRedirects(ast);
        else if (ast.Stages.Count == 1)
            exitCodes = [RunSingleStage(ast.Stages[0])];
        else
            exitCodes = RunPipeline(ast.Stages);

        _ctx.Vm.LastExitCode = exitCodes[^1];

        if (ast.IsStrict)
        {
            foreach (int code in exitCodes)
            {
                if (code != 0)
                    throw new RuntimeError(
                        $"Command failed with exit code {code}: {line.Trim()}",
                        null, StashErrorTypes.CommandError);
            }
        }
    }

    // ── Single-stage execution ───────────────────────────────────────────────

    private int RunSingleStage(ShellStage stage)
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

        return exitCode;
    }

    // ── Pipeline execution ───────────────────────────────────────────────────

    private int[] RunPipeline(IReadOnlyList<ShellStage> stages)
    {
        var resolved = new List<(string Program, List<string> Args)>(stages.Count);
        foreach (var stage in stages)
            resolved.Add(BuildArgv(stage));

        try
        {
            return _ctx.Vm.RunPassthroughPipeline(resolved, span: null);
        }
        catch (RuntimeError ex) when (IsSpawnFailure(ex))
        {
            throw WrapSpawnError(stages[0].Program, ex);
        }
    }

    // ── Redirected execution ─────────────────────────────────────────────────

    private int[] RunWithRedirects(ShellCommandLine ast)
    {
        var openedStreams = new List<Stream>();
        Stream? stdoutTarget = null;
        Stream? stderrTarget = null;
        bool stderrToStdout = false;

        try
        {
            foreach (var redirect in ast.Redirects)
            {
                string path = ExpandRedirectTarget(redirect.Target);

                var fileMode = redirect.Append ? FileMode.Append : FileMode.Create;
                FileStream fs;
                try
                {
                    fs = new FileStream(path, fileMode, FileAccess.Write, FileShare.Read);
                }
                catch (IOException ex)
                {
                    throw new RuntimeError(
                        $"redirect to '{path}' failed: {ex.Message}", null, StashErrorTypes.CommandError);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new RuntimeError(
                        $"redirect to '{path}' failed: {ex.Message}", null, StashErrorTypes.CommandError);
                }

                openedStreams.Add(fs);

                switch (redirect.Stream)
                {
                    case RedirectStream.Stdout:
                        stdoutTarget = fs;
                        stderrToStdout = false;
                        break;
                    case RedirectStream.Stderr:
                        stderrTarget = fs;
                        break;
                    case RedirectStream.Both:
                        stdoutTarget = fs;
                        stderrToStdout = true;
                        stderrTarget = null;
                        break;
                }
            }

            var resolved = new List<(string Program, List<string> Args)>(ast.Stages.Count);
            foreach (var stage in ast.Stages)
                resolved.Add(BuildArgv(stage));

            try
            {
                return _ctx.Vm.RunRedirectedPipeline(
                    resolved, stdoutTarget, stderrTarget, stderrToStdout, span: null);
            }
            catch (RuntimeError ex) when (IsSpawnFailure(ex))
            {
                throw WrapSpawnError(ast.Stages[0].Program, ex);
            }
        }
        finally
        {
            foreach (var s in openedStreams)
                try { s.Dispose(); } catch { }
        }
    }

    // ── Redirect target helpers ──────────────────────────────────────────────

    private static string ExpandRedirectTarget(string target)
    {
        if (string.IsNullOrEmpty(target)) return target;

        if (target == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (target.StartsWith("~/", StringComparison.Ordinal) ||
            target.StartsWith("~\\", StringComparison.Ordinal))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, target[2..]);
        }

        return target;
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
