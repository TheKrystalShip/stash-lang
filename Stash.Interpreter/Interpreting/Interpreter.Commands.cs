using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using Stash.Common;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Interpreting;

public partial class Interpreter
{
    /// <summary>
    /// Expands a leading tilde (~) in a path to the user's home directory.
    /// <c>~/foo</c> becomes <c>/home/user/foo</c>, <c>~</c> alone becomes <c>/home/user</c>.
    /// Tildes not at the start of the string are left untouched.
    /// </summary>
    internal static string ExpandTilde(string path)
    {
        if (path == "~")
        {
            return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            return System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                path.Substring(2));
        }

        return path;
    }

    /// <summary>
    /// Expands leading tildes in command strings. Handles both the command itself
    /// and arguments: <c>~/bin/tool ~/file.txt</c> → <c>/home/user/bin/tool /home/user/file.txt</c>.
    /// Only expands <c>~</c> followed by <c>/</c> or at end of a word, not inside quoted strings.
    /// </summary>
    private static string ExpandTildeInCommand(string command)
    {
        var sb = new StringBuilder(command.Length);
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool atWordStart = true;

        for (int i = 0; i < command.Length; i++)
        {
            char c = command[i];

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                sb.Append(c);
                atWordStart = false;
            }
            else if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                sb.Append(c);
                atWordStart = false;
            }
            else if (c == '~' && atWordStart && !inSingleQuote && !inDoubleQuote)
            {
                string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                // ~ alone or ~/path
                if (i + 1 >= command.Length || command[i + 1] == '/' || command[i + 1] == '\\' || char.IsWhiteSpace(command[i + 1]))
                {
                    sb.Append(home);
                }
                else
                {
                    sb.Append(c); // ~user syntax not supported, keep as-is
                }
                atWordStart = false;
            }
            else if (char.IsWhiteSpace(c))
            {
                sb.Append(c);
                atWordStart = true;
            }
            else
            {
                sb.Append(c);
                atWordStart = false;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Runs a process in passthrough mode — the child inherits the terminal's stdin, stdout,
    /// and stderr directly, allowing interactive programs and credential prompts to work.
    /// </summary>
    /// <param name="program">The executable to run.</param>
    /// <param name="arguments">The argument list.</param>
    /// <param name="span">Source span for error reporting.</param>
    /// <returns>A tuple of (stdout, stderr, exitCode). Stdout and stderr are always empty strings
    /// in passthrough mode since the streams are not captured.</returns>
    internal (string Stdout, string Stderr, int ExitCode) RunPassthrough(
        string program, List<string> arguments, SourceSpan span)
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
            process.WaitForExit();

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

    /// <summary>
    /// Runs a process in captured mode — stdout and stderr are redirected and read.
    /// Optionally provides data on stdin before closing the input stream.
    /// </summary>
    /// <param name="program">The executable to run.</param>
    /// <param name="arguments">The argument list.</param>
    /// <param name="stdin">Optional string to write to the process's stdin. Null means no stdin redirection.</param>
    /// <param name="span">Source span for error reporting.</param>
    /// <returns>A tuple of (stdout, stderr, exitCode) with the captured output.</returns>
    internal (string Stdout, string Stderr, int ExitCode) RunCaptured(
        string program, List<string> arguments, string? stdin, SourceSpan span)
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

            // Read stdout and stderr concurrently to avoid deadlock
            // when either stream's buffer fills.
            var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
            Task.WaitAll(stdoutTask, stderrTask);

            process.WaitForExit();

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

    /// <summary>
    /// Builds the command string from a <see cref="CommandExpr"/> by evaluating its parts,
    /// concatenating them, and expanding tildes.
    /// </summary>
    private string BuildCommandString(CommandExpr expr)
    {
        var commandBuilder = new StringBuilder();
        foreach (Expr part in expr.Parts)
        {
            object? value = part.Accept(this);
            commandBuilder.Append(Stringify(value));
        }

        string command = commandBuilder.ToString().Trim();
        command = ExpandTildeInCommand(command);

        if (string.IsNullOrEmpty(command))
        {
            throw new RuntimeError("Command cannot be empty.", expr.Span);
        }

        return command;
    }

    /// <summary>
    /// Evaluates a <see cref="CommandExpr"/> by building the command string from its parts,
    /// executing it via the system shell, and returning a <see cref="StashInstance"/> with
    /// <c>stdout</c>, <c>stderr</c>, and <c>exitCode</c> fields.
    /// </summary>
    /// <param name="expr">The command expression to evaluate.</param>
    /// <returns>A <see cref="StashInstance"/> representing the command result.</returns>
    public object? VisitCommandExpr(CommandExpr expr)
    {
        string command = BuildCommandString(expr);

        // Passthrough mode ($>): inherit terminal stdin/stdout/stderr directly.
        if (expr.IsPassthrough)
        {
            if (EmbeddedMode)
            {
                throw new RuntimeError("Passthrough commands are not available in embedded mode.", expr.Span);
            }

            if (_pendingStdin is not null)
            {
                throw new RuntimeError(
                    "Passthrough commands cannot receive piped input. Use a capture command $(...) instead.",
                    expr.Span);
            }

            var (program, arguments) = CommandParser.Parse(command);
            (program, arguments) = ApplyElevationPrefix(program, arguments);
            var (_, _, exitCode) = RunPassthrough(program, arguments, expr.Span);

            return new StashInstance("CommandResult", new Dictionary<string, object?>
            {
                ["stdout"] = "",
                ["stderr"] = "",
                ["exitCode"] = (long)exitCode
            }) { StringifyField = "stdout" };
        }

        var (capProgram, capArguments) = CommandParser.Parse(command);
        (capProgram, capArguments) = ApplyElevationPrefix(capProgram, capArguments);
        var (stdout, stderr, capExitCode) = RunCaptured(capProgram, capArguments, _pendingStdin, expr.Span);
        // Clear pending stdin after successful capture. In the pipe context, VisitPipeExpr's
        // finally block is the true cleanup owner. Any future caller that sets _pendingStdin
        // before calling RunCaptured must ensure cleanup in its own finally block.
        _pendingStdin = null;

        var fields = new Dictionary<string, object?>
        {
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["exitCode"] = (long)capExitCode
        };

        return new StashInstance("CommandResult", fields) { StringifyField = "stdout" };
    }

    /// <summary>
    /// Applies elevation prefix to a command if the elevation context is active
    /// and the program is not already an elevation command.
    /// </summary>
    private (string Program, List<string> Arguments) ApplyElevationPrefix(
        string program, List<string> arguments)
    {
        if (!_ctx.ElevationActive || _ctx.ElevationCommand is null)
            return (program, arguments);

        // Don't double-prefix commands that are already elevation commands
        string lowerProgram = program.ToLowerInvariant();
        if (lowerProgram is "sudo" or "doas" or "gsudo" or "runas" ||
            string.Equals(program, _ctx.ElevationCommand, StringComparison.OrdinalIgnoreCase))
        {
            return (program, arguments);
        }

        // Prefix: sudo ufw enable → FileName="sudo", Args=["ufw", "enable"]
        var prefixedArgs = new List<string>(arguments.Count + 1) { program };
        prefixedArgs.AddRange(arguments);
        return (_ctx.ElevationCommand, prefixedArgs);
    }

    /// <summary>
    /// Flattens a left-associative <see cref="PipeExpr"/> tree into an ordered list of
    /// command expressions. For example, <c>$(a) | $(b) | $(c)</c> which parses as
    /// <c>PipeExpr(PipeExpr(a, b), c)</c> becomes <c>[a, b, c]</c>.
    /// </summary>
    private static List<Expr> FlattenPipeChain(PipeExpr expr)
    {
        var commands = new List<Expr>();

        void Collect(Expr node)
        {
            if (node is PipeExpr pipe)
            {
                Collect(pipe.Left);
                Collect(pipe.Right);
            }
            else
            {
                commands.Add(node);
            }
        }

        Collect(expr);
        return commands;
    }

    /// <summary>
    /// Executes a pipeline of commands with streaming OS-level pipes connecting each
    /// process's stdout to the next process's stdin. All processes run concurrently.
    /// Returns the <c>CommandResult</c> from the last command in the pipeline.
    /// </summary>
    /// <param name="stages">The parsed (program, arguments) pairs for each stage.</param>
    /// <param name="stdin">Optional initial stdin for the first process (from an outer pipe).</param>
    /// <param name="span">Source span for error reporting.</param>
    /// <returns>A <see cref="StashInstance"/> with the last command's stdout, stderr, and exitCode.</returns>
    private StashInstance RunPipeline(
        List<(string Program, List<string> Arguments)> stages, string? stdin, SourceSpan span)
    {
        if (stages.Count == 0)
        {
            throw new RuntimeError("Pipeline cannot be empty.", span);
        }

        var processes = new List<Process>(stages.Count);
        var copyTasks = new List<Task>();

        try
        {
            // Start all processes with appropriate stream redirections.
            for (int i = 0; i < stages.Count; i++)
            {
                bool isFirst = i == 0;
                bool isLast = i == stages.Count - 1;
                var (program, arguments) = stages[i];

                var psi = new ProcessStartInfo
                {
                    FileName = program,
                    RedirectStandardInput = !isFirst || stdin is not null,
                    RedirectStandardOutput = true,
                    RedirectStandardError = isLast,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (string arg in arguments)
                {
                    psi.ArgumentList.Add(arg);
                }

                var process = Process.Start(psi)
                    ?? throw new RuntimeError($"Failed to start process: {program}", span);
                processes.Add(process);
            }

            // Wire up stdin for the first process if there's pending input from an outer pipe.
            if (stdin is not null && processes.Count > 0)
            {
                var firstProcess = processes[0];
                copyTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        firstProcess.StandardInput.Write(stdin);
                    }
                    catch (IOException)
                    {
                        // First process may exit before we finish writing stdin.
                    }
                    finally
                    {
                        try { firstProcess.StandardInput.Close(); } catch (IOException) { }
                    }
                }));
            }

            // Connect each process's stdout to the next process's stdin via async stream copy.
            for (int i = 0; i < processes.Count - 1; i++)
            {
                var source = processes[i];
                var target = processes[i + 1];

                copyTasks.Add(Task.Run(() =>
                {
                    try
                    {
                        source.StandardOutput.BaseStream.CopyTo(target.StandardInput.BaseStream);
                    }
                    catch (IOException)
                    {
                        // Broken pipe — the downstream process exited before the upstream
                        // finished writing. This is normal for pipelines like yes | head -5.
                        // Close the upstream's stdout so the upstream process gets SIGPIPE
                        // and terminates instead of blocking on a full pipe buffer.
                        try { source.StandardOutput.Close(); } catch { }
                    }
                    finally
                    {
                        try { target.StandardInput.Close(); } catch (IOException) { }
                    }
                }));
            }

            // Read the last process's stdout and stderr concurrently.
            var lastProcess = processes[^1];
            var stdoutTask = Task.Run(() => lastProcess.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => lastProcess.StandardError.ReadToEnd());

            // Wait for all stream copies and reads to complete.
            var allTasks = new List<Task>(copyTasks) { stdoutTask, stderrTask };
            Task.WaitAll(allTasks.ToArray());

            // Wait for all processes to exit.
            foreach (var process in processes)
            {
                process.WaitForExit();
            }

            // Pipeline exit code is the last command's exit code (POSIX default).
            int exitCode = processes[^1].ExitCode;

            return new StashInstance("CommandResult", new Dictionary<string, object?>
            {
                ["stdout"] = stdoutTask.Result,
                ["stderr"] = stderrTask.Result,
                ["exitCode"] = (long)exitCode
            }) { StringifyField = "stdout" };
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Pipeline execution failed: {ex.Message}", span);
        }
        finally
        {
            // Ensure all processes are cleaned up.
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    process.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }
    }

    /// <summary>
    /// Evaluates a pipe expression by launching all commands in the pipeline concurrently
    /// with streaming OS-level pipes connecting each process's stdout to the next's stdin.
    /// </summary>
    /// <param name="expr">The pipe expression node containing the left and right command sub-expressions.</param>
    /// <returns>
    /// A <see cref="StashInstance"/> (<c>CommandResult</c>) with the last command's stdout,
    /// stderr, and exit code. All commands run concurrently — there is no short-circuit on
    /// non-zero exit codes, matching POSIX pipeline semantics.
    /// </returns>
    public object? VisitPipeExpr(PipeExpr expr)
    {
        var commandExprs = FlattenPipeChain(expr);

        // Validate that all stages are capture command expressions.
        foreach (Expr stage in commandExprs)
        {
            if (stage is not CommandExpr)
            {
                throw new RuntimeError(
                    "All stages in a pipe must be command expressions.", stage.Span);
            }

            if (stage is CommandExpr { IsPassthrough: true })
            {
                throw new RuntimeError(
                    "Passthrough commands cannot be used in a pipeline. Use capture commands $(...) instead.",
                    stage.Span);
            }
        }

        // Build command strings and parse into (program, arguments) pairs.
        var stages = new List<(string Program, List<string> Arguments)>();
        foreach (Expr stage in commandExprs)
        {
            string command = BuildCommandString((CommandExpr)stage);
            var (program, arguments) = CommandParser.Parse(command);
            (program, arguments) = ApplyElevationPrefix(program, arguments);
            stages.Add((program, arguments));
        }

        // Use any pending stdin from an outer pipe as input to the first stage.
        string? initialStdin = _pendingStdin;
        _pendingStdin = null;

        return RunPipeline(stages, initialStdin, expr.Span);
    }

    /// <summary>
    /// Evaluates a redirect expression by executing the command and writing the selected
    /// stream(s) to the target file path.
    /// </summary>
    /// <param name="expr">The redirect expression node describing the command, target path, stream, and append flag.</param>
    /// <returns>
    /// A new <see cref="StashInstance"/> (<c>CommandResult</c>) with the redirected stream(s)
    /// cleared to empty strings, preserving the original exit code.
    /// </returns>
    /// <remarks>
    /// Supports three stream selectors: <c>stdout</c>, <c>stderr</c>, and <c>all</c> (both).
    /// When <see cref="RedirectExpr.Append"/> is <c>true</c> the content is appended to the
    /// file rather than overwriting it. Passthrough commands (<c>$&gt;(...)</c>) cannot be
    /// redirected. Any I/O failure is reported as a <see cref="RuntimeError"/>.
    /// </remarks>
    public object? VisitRedirectExpr(RedirectExpr expr)
    {
        // Check if the inner expression is a passthrough command — these can't be redirected
        // since their output goes directly to the terminal.
        if (expr.Expression is CommandExpr { IsPassthrough: true })
        {
            throw new RuntimeError(
                "Passthrough commands cannot be redirected. Use a capture command $(...) instead.",
                expr.Span);
        }

        // Evaluate the inner command/pipe expression
        object? result = expr.Expression.Accept(this);

        if (result is not StashInstance cmdResult || cmdResult.TypeName != "CommandResult")
        {
            throw new RuntimeError("Output redirection requires a command expression.", expr.Span);
        }

        // Evaluate the target file path
        object? targetVal = expr.Target.Accept(this);
        if (targetVal is not string filePath)
        {
            throw new RuntimeError("Redirection target must be a string file path.", expr.Target.Span);
        }

        string? stdoutContent = cmdResult.GetField("stdout", expr.Span) as string;
        string? stderrContent = cmdResult.GetField("stderr", expr.Span) as string;

        try
        {
            // Determine which content to write based on the stream selector
            string contentToWrite = expr.Stream switch
            {
                RedirectStream.Stdout => stdoutContent ?? "",
                RedirectStream.Stderr => stderrContent ?? "",
                RedirectStream.All => (stdoutContent ?? "") + (stderrContent ?? ""),
                _ => throw new RuntimeError($"Unknown redirect stream: {expr.Stream}.", expr.Span)
            };

            if (expr.Append)
            {
                File.AppendAllText(filePath, contentToWrite);
            }
            else
            {
                File.WriteAllText(filePath, contentToWrite);
            }

            // Clear the redirected stream(s) in the result since they went to a file.
            // Return a new CommandResult with the redirected stream(s) emptied.
            var newFields = new Dictionary<string, object?>
            {
                ["stdout"] = expr.Stream is RedirectStream.Stdout or RedirectStream.All ? "" : stdoutContent,
                ["stderr"] = expr.Stream is RedirectStream.Stderr or RedirectStream.All ? "" : stderrContent,
                ["exitCode"] = cmdResult.GetField("exitCode", expr.Span)
            };

            return new StashInstance("CommandResult", newFields) { StringifyField = "stdout" };
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Redirection failed: {ex.Message}", expr.Span);
        }
    }
}
