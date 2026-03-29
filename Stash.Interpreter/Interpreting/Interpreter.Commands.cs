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
    /// Evaluates a <see cref="CommandExpr"/> by building the command string from its parts,
    /// executing it via the system shell, and returning a <see cref="StashInstance"/> with
    /// <c>stdout</c>, <c>stderr</c>, and <c>exitCode</c> fields.
    /// </summary>
    /// <param name="expr">The command expression to evaluate.</param>
    /// <returns>A <see cref="StashInstance"/> representing the command result.</returns>
    public object? VisitCommandExpr(CommandExpr expr)
    {
        var commandBuilder = new StringBuilder();
        foreach (Expr part in expr.Parts)
        {
            object? value = part.Accept(this);
            commandBuilder.Append(Stringify(value));
        }

        string command = commandBuilder.ToString().Trim();

        // Expand tilde (~) to home directory in command arguments
        command = ExpandTildeInCommand(command);

        if (string.IsNullOrEmpty(command))
        {
            throw new RuntimeError("Command cannot be empty.", expr.Span);
        }

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
            });
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

        return new StashInstance("CommandResult", fields);
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
    /// Evaluates a pipe expression by chaining stdout of the left command to stdin of the right.
    /// Short-circuits on non-zero exit code.
    /// </summary>
    /// <param name="expr">The pipe expression node containing the left and right command sub-expressions.</param>
    /// <returns>
    /// The <see cref="StashInstance"/> (<c>CommandResult</c>) produced by the right-hand command
    /// on success, or the left-hand result unchanged if its exit code is non-zero.
    /// </returns>
    /// <remarks>
    /// The left command must evaluate to a <c>CommandResult</c> instance. Its <c>stdout</c>
    /// field is passed as <c>stdin</c> to the right command. If the left command's exit code
    /// is non-zero the right command is not executed and the left result is returned as-is,
    /// allowing callers to inspect the failure.
    /// </remarks>
    public object? VisitPipeExpr(PipeExpr expr)
    {
        object? leftResult = expr.Left.Accept(this);

        if (leftResult is not StashInstance leftCmd || leftCmd.TypeName != "CommandResult")
        {
            throw new RuntimeError("Left side of pipe must be a command expression.", expr.Span);
        }

        object? exitCodeVal = leftCmd.GetField("exitCode", expr.Span);
        if (exitCodeVal is long exitCode && exitCode != 0)
        {
            return leftResult;
        }

        object? stdoutVal = leftCmd.GetField("stdout", expr.Span);
        string stdinForRight = stdoutVal as string ?? "";

        _pendingStdin = stdinForRight;

        try
        {
            object? rightResult = expr.Right.Accept(this);

            if (rightResult is not StashInstance rightCmd || rightCmd.TypeName != "CommandResult")
            {
                throw new RuntimeError("Right side of pipe must be a command expression.", expr.Span);
            }

            return rightResult;
        }
        finally
        {
            _pendingStdin = null;
        }
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

            return new StashInstance("CommandResult", newFields);
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
