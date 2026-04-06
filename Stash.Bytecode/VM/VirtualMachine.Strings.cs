using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// String interpolation, command execution, pipe, and redirect opcode handlers.
/// </summary>
public sealed partial class VirtualMachine
{
    private void ExecuteCommand(ref CallFrame frame)
    {
        ushort metaCmdIdx = ReadU16(ref frame);
        SourceSpan? span = GetCurrentSpan(ref frame);
        var cmdMetadata = (CommandMetadata)frame.Chunk.Constants[metaCmdIdx].AsObj!;

        var sb = new StringBuilder();
        int partStart = _sp - cmdMetadata.PartCount;
        for (int i = partStart; i < _sp; i++)
        {
            sb.Append(RuntimeOps.Stringify(_stack[i]));
        }

        _sp = partStart;

        string command = _context.ExpandTilde(sb.ToString().Trim());
        if (string.IsNullOrEmpty(command))
        {
            throw new RuntimeError("Command cannot be empty.", span);
        }

        var (program, arguments) = CommandParser.Parse(command);

        // Expand tilde in individual arguments (the full-command ExpandTilde above
        // only handles a leading ~ in the program name)
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (int i = 0; i < arguments.Count; i++)
        {
            string arg = arguments[i];
            if (arg == "~")
            {
                arguments[i] = home;
            }
            else if (arg.StartsWith("~/", StringComparison.Ordinal) || arg.StartsWith("~\\", StringComparison.Ordinal))
            {
                arguments[i] = Path.Combine(home, arg[2..]);
            }
        }

        // Apply elevation prefix if active
        if (_context.ElevationActive && _context.ElevationCommand != null)
        {
            string lowerProgram = program.ToLowerInvariant();
            if (lowerProgram is not ("sudo" or "doas" or "gsudo" or "runas") &&
                !string.Equals(program, _context.ElevationCommand, StringComparison.OrdinalIgnoreCase))
            {
                var prefixedArgs = new List<string>(arguments.Count + 1) { program };
                prefixedArgs.AddRange(arguments);
                arguments = prefixedArgs;
                program = _context.ElevationCommand;
            }
        }

        if (cmdMetadata.IsPassthrough)
        {
            var (_, _, exitCode) = ExecPassthrough(program, arguments, span);
            if (cmdMetadata.IsStrict && exitCode != 0)
            {
                throw new RuntimeError(
                    $"Command failed with exit code {exitCode}: {command}",
                    span, "CommandError")
                {
                    Properties = new Dictionary<string, object?>
                    {
                        ["exitCode"] = (long)exitCode,
                        ["stderr"] = "",
                        ["stdout"] = "",
                        ["command"] = command
                    }
                };
            }
            Push(StashValue.FromObj(new StashInstance("CommandResult", new Dictionary<string, object?>
            {
                ["stdout"] = "",
                ["stderr"] = "",
                ["exitCode"] = (long)exitCode
            }) { StringifyField = "stdout" }));
        }
        else
        {
            var (stdout, stderr, exitCode) = ExecCaptured(program, arguments, null, span);
            if (cmdMetadata.IsStrict && exitCode != 0)
            {
                throw new RuntimeError(
                    $"Command failed with exit code {exitCode}: {command}",
                    span, "CommandError")
                {
                    Properties = new Dictionary<string, object?>
                    {
                        ["exitCode"] = (long)exitCode,
                        ["stderr"] = stderr,
                        ["stdout"] = stdout,
                        ["command"] = command
                    }
                };
            }
            Push(StashValue.FromObj(new StashInstance("CommandResult", new Dictionary<string, object?>
            {
                ["stdout"] = stdout,
                ["stderr"] = stderr,
                ["exitCode"] = (long)exitCode
            }) { StringifyField = "stdout" }));
        }
    }

    private void ExecutePipe(ref CallFrame frame)
    {
        // Both sides have already been executed. Return the right result, which
        // carries the final exit code per pipeline semantics.
        // True streaming pipes are a future enhancement (Phase 7+).
        StashValue rightResult = Pop();
        StashValue leftResult = Pop();

        static bool IsCommandResult(object? obj) =>
            obj is StashDictionary d && d.Has("stdout") && d.Has("exitCode");

        if (!IsCommandResult(leftResult.ToObject()) || !IsCommandResult(rightResult.ToObject()))
        {
            throw new RuntimeError("All stages in a pipe must be command expressions.", GetCurrentSpan(ref frame));
        }

        Push(rightResult);
    }

    private void ExecuteRedirect(ref CallFrame frame)
    {
        byte flags = ReadByte(ref frame);
        SourceSpan? span = GetCurrentSpan(ref frame);
        object? target = Pop().ToObject();
        object? cmdResult = Pop().ToObject();

        string filePath = target is string fp
            ? fp
            : throw new RuntimeError("Redirect target must be a string.", span);

        int stream = flags & 0x03;
        bool append = (flags & 0x04) != 0;

        string stdout = "", stderr = "";
        if (cmdResult is StashInstance ri)
        {
            stdout = (ri.GetField("stdout", span) as string) ?? "";
            stderr = (ri.GetField("stderr", span) as string) ?? "";
        }

        string content = stream switch
        {
            0 => stdout,
            1 => stderr,
            _ => stdout + stderr
        };

        try
        {
            if (append)
            {
                File.AppendAllText(filePath, content);
            }
            else
            {
                File.WriteAllText(filePath, content);
            }
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Redirect failed: {ex.Message}", span);
        }

        var newFields = new Dictionary<string, object?>
        {
            ["stdout"] = (stream == 0 || stream == 2) ? "" : stdout,
            ["stderr"] = (stream == 1 || stream == 2) ? "" : stderr,
            ["exitCode"] = cmdResult is StashInstance ri2 ? ri2.GetField("exitCode", span) : 0L
        };
        Push(StashValue.FromObj(new StashInstance("CommandResult", newFields) { StringifyField = "stdout" }));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteInterpolate(ref CallFrame frame)
    {
        ushort count = ReadU16(ref frame);
        string result = RuntimeOps.Interpolate(_stack, _sp, count);
        _sp -= count;
        Push(StashValue.FromObj(result));
    }
}
