namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Runtime.Errors;

/// <summary>
/// Shared implementations for current-process built-ins exposed via both
/// the canonical <c>env.*</c> namespace and the deprecated <c>process.*</c> aliases.
/// </summary>
internal static class CurrentProcessImpl
{
    public static StashValue Chdir(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var path = SvArgs.String(args, 0, callerQualified);

        string expanded = ctx.ExpandTilde(path);
        string resolved = ctx.ResolveAgainstCwd(expanded);
        if (!System.IO.Directory.Exists(resolved))
        {
            throw new CommandError($"no such directory: {resolved}", exitCode: -1);
        }

        // Cap the stack at 256 entries: drop the eldest (index 0) to make room.
        var stack = ctx.DirStack;
        if (stack.Count >= 256)
        {
            stack.RemoveAt(0);
        }

        // Atomic: update per-VM WorkingDirectory; only push to the stack if the change succeeds.
        // System.Environment.CurrentDirectory is never mutated — each VM has its own overlay.
        ctx.WorkingDirectory = resolved;
        stack.Add(resolved);
        return StashValue.Null;
    }

    public static StashValue PopDir(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var stack = ctx.DirStack;
        if (stack.Count <= 1)
        {
            throw new CommandError("directory stack is at root", exitCode: -1);
        }

        string popped = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        string newTop = stack[^1];
        // Update per-VM WorkingDirectory only — real process cwd is never mutated.
        ctx.WorkingDirectory = newTop;
        return StashValue.FromObj(popped);
    }

    public static StashValue DirStack(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var stack = ctx.DirStack;
        var result = new List<StashValue>(stack.Count);
        foreach (string dir in stack)
        {
            result.Add(StashValue.FromObj(dir));
        }
        return StashValue.FromObj(result);
    }

    public static StashValue DirStackDepth(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        return StashValue.FromInt((long)ctx.DirStack.Count);
    }

    public static StashValue WithDir(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var path = SvArgs.String(args, 0, callerQualified);
        var fn = SvArgs.Callable(args, 1, callerQualified);

        string resolved = ctx.ResolveAgainstCwd(path);
        if (!System.IO.Directory.Exists(resolved))
        {
            throw new IOError($"{callerQualified}: directory does not exist: '{resolved}'.");
        }

        string previous = ctx.WorkingDirectory;
        // Update per-VM WorkingDirectory only — real process cwd is never mutated.
        ctx.WorkingDirectory = resolved;
        try
        {
            return ctx.InvokeCallbackDirect(fn, ReadOnlySpan<StashValue>.Empty);
        }
        finally
        {
            ctx.WorkingDirectory = previous;
        }
    }
}
