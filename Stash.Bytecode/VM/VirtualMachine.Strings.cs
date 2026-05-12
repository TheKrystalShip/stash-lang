using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Stash.Bytecode;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// String interpolation, command execution, pipe, and redirect opcode handlers.
/// </summary>
public sealed partial class VirtualMachine
{
    [MethodImpl(MethodImplOptions.NoInlining)]
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

        var parsedArgs = CommandParser.ParseWithQuotedFlags(command);
        string program = parsedArgs.Count > 0 ? parsedArgs[0].Token : "";
        var arguments = new List<string>(parsedArgs.Count > 1 ? parsedArgs.Count - 1 : 0);
        var quotedFlags = new List<bool>(parsedArgs.Count > 1 ? parsedArgs.Count - 1 : 0);
        for (int i = 1; i < parsedArgs.Count; i++)
        {
            arguments.Add(parsedArgs[i].Token);
            quotedFlags.Add(parsedArgs[i].WasQuoted);
        }

        ApplyTildeToArguments(arguments);
        ApplyGlobExpansion(arguments, quotedFlags, span);
        ApplyElevationIfActive(ref program, ref arguments);

        bool isPassthrough = (flags & 0x01) != 0;
        bool isStrict      = (flags & 0x02) != 0;
        bool isStreaming   = (flags & 0x04) != 0;

        if (isStreaming)
        {
            // Note: in-paren pipe chains like $<(a | b | c) are split by the lexer into
            // separate streaming-command literals joined by Pipe tokens, then compiled to
            // OpCode.StreamingPipeline. By the time we reach ExecuteCommand, the command
            // string for a single-stage streaming literal contains no top-level pipe.
            var streamHandle = ExecStreaming(program, arguments, command, isStrict, span);
            _stack[@base + a] = StashValue.FromObj(streamHandle);
            return;
        }

        if (isPassthrough)
        {
            var (_, _, exitCode) = ExecPassthrough(program, arguments, span, _ct);
            if (isStrict && exitCode != 0)
            {
                throw new CommandError(
                    $"Command failed with exit code {exitCode}: {command}",
                    (long)exitCode, "", "", command, span);
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
                throw new CommandError(
                    $"Command failed with exit code {exitCode}: {command}",
                    (long)exitCode, stderr, stdout, command, span);
            }
            _stack[@base + a] = StashValue.FromObj(new StashInstance("CommandResult", new Dictionary<string, StashValue>
            {
                ["stdout"] = StashValue.FromObj(stdout),
                ["stderr"] = StashValue.FromObj(stderr),
                ["exitCode"] = StashValue.FromInt((long)exitCode)
            }) { StringifyField = "stdout" });
        }
    }

    private static void ApplyGlobExpansion(List<string> arguments, List<bool> wasQuoted, SourceSpan? span)
    {
        // Iterate backwards so insertions don't invalidate earlier indices
        for (int i = arguments.Count - 1; i >= 0; i--)
        {
            if (wasQuoted[i]) continue;
            if (!GlobExpander.HasGlobChars(arguments[i])) continue;

            var matches = GlobExpander.Expand(arguments[i]);
            if (matches.Count == 0)
                throw new CommandError(
                    $"glob pattern '{arguments[i]}' did not match any files",
                    1, span: span);

            // Replace arg[i] with all matched paths
            arguments.RemoveAt(i);
            wasQuoted.RemoveAt(i);
            for (int j = 0; j < matches.Count; j++)
            {
                arguments.Insert(i + j, matches[j]);
                wasQuoted.Insert(i + j, false);
            }
        }
    }

    private static void ApplyTildeToArguments(List<string> arguments)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        for (int i = 0; i < arguments.Count; i++)
        {
            string arg = arguments[i];
            if (arg == "~")
                arguments[i] = home;
            else if (arg.StartsWith("~/", StringComparison.Ordinal) || arg.StartsWith("~\\", StringComparison.Ordinal))
                arguments[i] = Path.Combine(home, arg[2..]);
        }
    }

    private void ApplyElevationIfActive(ref string program, ref List<string> arguments)
    {
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
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecutePipeChain(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte stageCount = Instruction.GetB(inst);
        byte partsBase = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        SourceSpan? span = GetCurrentSpan(ref frame);

        // 1. Read B companion words from the instruction stream
        var stageMetas = new (int PartCount, byte Flags)[stageCount];
        for (int i = 0; i < stageCount; i++)
        {
            uint companion = frame.Chunk.Code[frame.IP++];
            stageMetas[i] = ((int)((companion >> 8) & 0xFF), (byte)(companion & 0xFF));
        }

        // 2. Assemble stage descriptors from the parts register block
        var stages = new List<PipeStage>(stageCount);
        int regOffset = 0;
        Span<char> stackBuf = stackalloc char[256];
        for (int i = 0; i < stageCount; i++)
        {
            var (partCount, flags) = stageMetas[i];

            var vsb = new ValueStringBuilder(stackBuf);
            for (int p = 0; p < partCount; p++)
                vsb.Append(RuntimeOps.Stringify(_stack[@base + partsBase + regOffset + p]));
            regOffset += partCount;

            string command = _context.ExpandTilde(vsb.AsSpan().Trim().ToString());
            vsb.Dispose();
            if (string.IsNullOrEmpty(command))
                throw new RuntimeError("Command cannot be empty in pipe chain.", span);

            var parsedStage = CommandParser.ParseWithQuotedFlags(command);
            string program = parsedStage.Count > 0 ? parsedStage[0].Token : "";
            var arguments = new List<string>(parsedStage.Count > 1 ? parsedStage.Count - 1 : 0);
            var quotedFlags = new List<bool>(parsedStage.Count > 1 ? parsedStage.Count - 1 : 0);
            for (int si = 1; si < parsedStage.Count; si++)
            {
                arguments.Add(parsedStage[si].Token);
                quotedFlags.Add(parsedStage[si].WasQuoted);
            }
            ApplyTildeToArguments(arguments);
            ApplyGlobExpansion(arguments, quotedFlags, span);
            ApplyElevationIfActive(ref program, ref arguments);

            stages.Add(new PipeStage(program, arguments, flags));
        }

        // 3. Execute the streaming pipeline
        var (stdout, stderr, exitCodes) = ExecPipelineStreaming(stages, span, _ct);

        // 4. Strict mode check on last stage only
        byte lastFlags = stageMetas[stageCount - 1].Flags;
        bool isStrict = (lastFlags & 0x01) != 0;
        int lastExitCode = exitCodes[^1];
        if (isStrict && lastExitCode != 0)
        {
            throw new CommandError(
                $"Command failed with exit code {lastExitCode}.",
                (long)lastExitCode, stderr, stdout, span: span);
        }

        // 5. Store CommandResult
        _stack[@base + a] = StashValue.FromObj(new StashInstance("CommandResult",
            new Dictionary<string, StashValue>
            {
                ["stdout"]   = StashValue.FromObj(stdout),
                ["stderr"]   = StashValue.FromObj(stderr),
                ["exitCode"] = StashValue.FromInt((long)lastExitCode)
            }) { StringifyField = "stdout" });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteStreamingPipeline(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte stageCount = Instruction.GetB(inst);
        byte partsBase = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        SourceSpan? span = GetCurrentSpan(ref frame);

        // 1. Read B companion words from the instruction stream
        var stageMetas = new (int PartCount, byte Flags)[stageCount];
        for (int i = 0; i < stageCount; i++)
        {
            uint companion = frame.Chunk.Code[frame.IP++];
            stageMetas[i] = ((int)((companion >> 8) & 0xFF), (byte)(companion & 0xFF));
        }

        // 2. Assemble stage descriptors from the parts register block
        var stages = new List<PipeStage>(stageCount);
        int regOffset = 0;
        Span<char> stackBuf = stackalloc char[256];
        var commandBuf = new System.Text.StringBuilder();
        for (int i = 0; i < stageCount; i++)
        {
            var (partCount, flags) = stageMetas[i];

            var vsb = new ValueStringBuilder(stackBuf);
            for (int p = 0; p < partCount; p++)
                vsb.Append(RuntimeOps.Stringify(_stack[@base + partsBase + regOffset + p]));
            regOffset += partCount;

            string command = _context.ExpandTilde(vsb.AsSpan().Trim().ToString());
            vsb.Dispose();
            if (string.IsNullOrEmpty(command))
                throw new RuntimeError("Command cannot be empty in streaming pipe chain.", span);

            if (commandBuf.Length > 0) commandBuf.Append(" | ");
            commandBuf.Append(command);

            var parsedStage = CommandParser.ParseWithQuotedFlags(command);
            string program = parsedStage.Count > 0 ? parsedStage[0].Token : "";
            var arguments = new List<string>(parsedStage.Count > 1 ? parsedStage.Count - 1 : 0);
            var quotedFlags = new List<bool>(parsedStage.Count > 1 ? parsedStage.Count - 1 : 0);
            for (int si = 1; si < parsedStage.Count; si++)
            {
                arguments.Add(parsedStage[si].Token);
                quotedFlags.Add(parsedStage[si].WasQuoted);
            }
            ApplyTildeToArguments(arguments);
            ApplyGlobExpansion(arguments, quotedFlags, span);
            ApplyElevationIfActive(ref program, ref arguments);

            stages.Add(new PipeStage(program, arguments, flags));
        }

        // 3. Pipeline-level strict flag is encoded on the LAST stage's flags byte (bit 0x01).
        bool isStrict = (stageMetas[stageCount - 1].Flags & 0x01) != 0;

        // 4. Spawn the pipeline; the last stage's stdout streams through the handle.
        var handle = ExecStreamingPipeline(stages, isStrict, commandBuf.ToString(), span);
        _stack[@base + a] = StashValue.FromObj(handle);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
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

    [MethodImpl(MethodImplOptions.NoInlining)]
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
