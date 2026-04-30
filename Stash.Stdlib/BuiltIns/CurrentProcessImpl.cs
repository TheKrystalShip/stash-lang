namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;

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
        string resolved = System.IO.Path.GetFullPath(expanded);
        if (!System.IO.Directory.Exists(resolved))
        {
            throw new RuntimeError($"no such directory: {resolved}", errorType: StashErrorTypes.CommandError);
        }

        // Cap the stack at 256 entries: drop the eldest (index 0) to make room.
        var stack = ctx.DirStack;
        if (stack.Count >= 256)
        {
            stack.RemoveAt(0);
        }

        // Atomic: change cwd first; only push to the stack if the change succeeds.
        System.Environment.CurrentDirectory = resolved;
        stack.Add(resolved);
        return StashValue.Null;
    }

    public static StashValue PopDir(IInterpreterContext ctx, ReadOnlySpan<StashValue> args, string callerQualified)
    {
        var stack = ctx.DirStack;
        if (stack.Count <= 1)
        {
            throw new RuntimeError("directory stack is at root", errorType: StashErrorTypes.CommandError);
        }

        string popped = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        string newTop = stack[^1];
        System.Environment.CurrentDirectory = newTop;
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

        string resolved = System.IO.Path.GetFullPath(path);
        if (!System.IO.Directory.Exists(resolved))
        {
            throw new RuntimeError($"{callerQualified}: directory does not exist: '{resolved}'.", errorType: StashErrorTypes.IOError);
        }

        string previous = System.Environment.CurrentDirectory;
        System.Environment.CurrentDirectory = resolved;
        try
        {
            return ctx.InvokeCallbackDirect(fn, ReadOnlySpan<StashValue>.Empty);
        }
        finally
        {
            System.Environment.CurrentDirectory = previous;
        }
    }
}
