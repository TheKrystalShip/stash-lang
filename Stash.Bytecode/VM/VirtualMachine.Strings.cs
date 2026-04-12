using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// String interpolation, command execution, pipe, and redirect opcode handlers.
/// </summary>
public sealed partial class VirtualMachine
{
    private void ExecuteCommand(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte partCount = Instruction.GetB(inst);
        byte flags = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        SourceSpan? span = GetCurrentSpan(ref frame);

        // Parts are in R(A+1)..R(A+partCount)
        Span<char> stackBuf = stackalloc char[256];
        var vsb = new ValueStringBuilder(stackBuf);
        for (int i = 1; i <= partCount; i++)
            vsb.Append(RuntimeOps.Stringify(_stack[@base + a + i]));

        string command = _context.ExpandTilde(vsb.AsSpan().Trim().ToString());
        vsb.Dispose();
        if (string.IsNullOrEmpty(command))
            throw new RuntimeError("Command cannot be empty.", span);

        var (program, arguments) = CommandParser.Parse(command);

        // Expand tilde in individual arguments
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (int i = 0; i < arguments.Count; i++)
        {
            string arg = arguments[i];
            if (arg == "~")
                arguments[i] = home;
            else if (arg.StartsWith("~/", StringComparison.Ordinal) || arg.StartsWith("~\\", StringComparison.Ordinal))
                arguments[i] = Path.Combine(home, arg[2..]);
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

        bool isPassthrough = (flags & 0x01) != 0;
        bool isStrict      = (flags & 0x02) != 0;

        if (isPassthrough)
        {
            var (_, _, exitCode) = ExecPassthrough(program, arguments, span, _ct);
            if (isStrict && exitCode != 0)
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
            _stack[@base + a] = StashValue.FromObj(new StashInstance("CommandResult", new Dictionary<string, StashValue>
            {
                ["stdout"] = StashValue.FromObj(""),
                ["stderr"] = StashValue.FromObj(""),
                ["exitCode"] = StashValue.FromInt((long)exitCode)
            }) { StringifyField = "stdout" });
        }
        else
        {
            var (stdout, stderr, exitCode) = ExecCaptured(program, arguments, null, span, _ct);
            if (isStrict && exitCode != 0)
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
            _stack[@base + a] = StashValue.FromObj(new StashInstance("CommandResult", new Dictionary<string, StashValue>
            {
                ["stdout"] = StashValue.FromObj(stdout),
                ["stderr"] = StashValue.FromObj(stderr),
                ["exitCode"] = StashValue.FromInt((long)exitCode)
            }) { StringifyField = "stdout" });
        }
    }

    private void ExecutePipe(ref CallFrame frame, uint inst)
    {
        // ABC: R(A) = pipe(R(B), R(C)) — both sides already evaluated
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;

        static bool IsCommandResult(object? obj) =>
            obj is StashDictionary d && d.Has("stdout") && d.Has("exitCode");

        object? leftResult  = _stack[@base + b].ToObject();
        object? rightResult = _stack[@base + c].ToObject();

        if (!IsCommandResult(leftResult) || !IsCommandResult(rightResult))
            throw new RuntimeError("All stages in a pipe must be command expressions.", GetCurrentSpan(ref frame));

        _stack[@base + a] = _stack[@base + c]; // result is the right side
    }

    private void ExecuteRedirect(ref CallFrame frame, uint inst)
    {
        // ABC: Redirect R(A) stream (B=flags) to file R(C); result back in R(A)
        byte a = Instruction.GetA(inst);
        byte flags = Instruction.GetB(inst);
        byte c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        SourceSpan? span = GetCurrentSpan(ref frame);

        object? target    = _stack[@base + c].ToObject();
        object? cmdResult = _stack[@base + a].ToObject();

        string filePath = target is string fp
            ? fp
            : throw new RuntimeError("Redirect target must be a string.", span);

        int stream = flags & 0x03;
        bool append = (flags & 0x04) != 0;

        string stdout = "", stderr = "";
        if (cmdResult is StashInstance ri)
        {
            stdout = (ri.GetField("stdout", span).ToObject() as string) ?? "";
            stderr = (ri.GetField("stderr", span).ToObject() as string) ?? "";
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
                File.AppendAllText(filePath, content);
            else
                File.WriteAllText(filePath, content);
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Redirect failed: {ex.Message}", span);
        }

        var newFields = new Dictionary<string, StashValue>
        {
            ["stdout"] = StashValue.FromObj((stream == 0 || stream == 2) ? "" : stdout),
            ["stderr"] = StashValue.FromObj((stream == 1 || stream == 2) ? "" : stderr),
            ["exitCode"] = cmdResult is StashInstance ri2 ? ri2.GetField("exitCode", span) : StashValue.Zero
        };
        _stack[@base + a] = StashValue.FromObj(new StashInstance("CommandResult", newFields) { StringifyField = "stdout" });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteInterpolate(ref CallFrame frame, uint inst)
    {
        // ABC: R(A) = interpolate B parts from R(A+1)..R(A+B)
        byte a = Instruction.GetA(inst);
        byte partCount = Instruction.GetB(inst);
        int @base = frame.BaseSlot;

        // Build the interpolated string from register slice R(A+1)..R(A+partCount)
        // RuntimeOps.Interpolate(stack, sp, count) reads stack[sp-count..sp-1]
        string result = RuntimeOps.Interpolate(_stack, @base + a + 1 + partCount, partCount);
        _stack[@base + a] = StashValue.FromObj(result);
    }
}
