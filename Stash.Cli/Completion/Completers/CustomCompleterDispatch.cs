using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Cli.Completion.Completers;

/// <summary>
/// Dispatches to user-registered custom completers for shell-mode argument completion.
/// Activated when a custom completer is registered for the current command name.
/// <para>
/// The registered Stash callable is invoked with a single <c>CompletionContext</c>
/// struct argument. The return value is converted to <see cref="Candidate"/> instances:
/// <list type="bullet">
///   <item>Array of strings → <see cref="CandidateKind.Custom"/> candidates.</item>
///   <item>Array of dicts/structs with <c>display</c> and <c>insert</c> fields → those fields used.</item>
///   <item><c>null</c> or empty array → returns <c>null</c> (signal to fall back to PathCompleter).</item>
/// </list>
/// </para>
/// <para>
/// Exceptions are caught; the error is logged once per session per command name via
/// <see cref="CustomCompleterRegistry.RecordError"/> and the method returns <c>null</c>.
/// </para>
/// </summary>
internal sealed class CustomCompleterDispatch
{
    /// <summary>
    /// Tries to invoke the registered custom completer for <paramref name="commandName"/>.
    /// Returns <c>null</c> when no completer is registered, when the completer returns null/empty,
    /// or when the completer throws (after logging the error once).
    /// </summary>
    public IReadOnlyList<Candidate>? TryDispatch(CursorContext ctx, CompletionDeps deps, string commandName)
    {
        IStashCallable? callable = deps.CustomCompleters.Get(commandName);
        if (callable is null)
            return null;

        StashInstance ctxInstance = BuildCompletionContext(ctx, commandName);

        try
        {
            var vmCtx = deps.Vm.Context;
            StashValue result = vmCtx.InvokeCallbackDirect(
                callable,
                [StashValue.FromObj(ctxInstance)]);

            return ConvertResult(result);
        }
        catch (Exception ex)
        {
            if (!deps.CustomCompleters.HasReportedError(commandName))
                Console.Error.WriteLine($"completer for '{commandName}' failed: {ex.Message}");
            deps.CustomCompleters.RecordError(commandName);
            return null;
        }
    }

    private static StashInstance BuildCompletionContext(CursorContext ctx, string commandName)
    {
        // Build the args array from prior args (best-effort)
        var argsArray = new List<StashValue>(ctx.PriorArgs.Count);
        foreach (string arg in ctx.PriorArgs)
            argsArray.Add(StashValue.FromObj(arg));

        string modeStr = ctx.Mode switch
        {
            CompletionMode.Shell => "shell",
            CompletionMode.Stash => "stash",
            CompletionMode.Substitution => "substitution",
            _ => "shell"
        };

        var fields = new Dictionary<string, StashValue>
        {
            ["command"]  = StashValue.FromObj(commandName),
            ["args"]     = StashValue.FromObj(argsArray),
            ["current"]  = StashValue.FromObj(ctx.TokenText),
            ["position"] = StashValue.FromInt(ctx.PriorArgs.Count + 1),
            ["mode"]     = StashValue.FromObj(modeStr),
        };

        return new StashInstance("CompletionContext", fields);
    }

    private static IReadOnlyList<Candidate>? ConvertResult(StashValue result)
    {
        object? obj = result.ToObject();

        if (obj is null)
            return null;

        if (obj is List<StashValue> list)
        {
            if (list.Count == 0)
                return null;

            var candidates = new List<Candidate>(list.Count);
            foreach (StashValue item in list)
            {
                object? itemObj = item.ToObject();

                if (itemObj is string s)
                {
                    candidates.Add(new Candidate(s, s, CandidateKind.Custom));
                    continue;
                }

                // Dict or struct with display/insert fields
                string? display = null;
                string? insert = null;

                if (itemObj is StashDictionary dict)
                {
                    object? dispObj = dict.Get("display").ToObject();
                    object? insObj = dict.Get("insert").ToObject();
                    if (dispObj is string d) display = d;
                    if (insObj is string i) insert = i;
                }
                else if (itemObj is StashInstance inst)
                {
                    try
                    {
                        object? dispObj = inst.GetField("display", null).ToObject();
                        object? insObj = inst.GetField("insert", null).ToObject();
                        if (dispObj is string d) display = d;
                        if (insObj is string i) insert = i;
                    }
                    catch { /* missing field — skip */ }
                }

                if (display != null && insert != null)
                    candidates.Add(new Candidate(display, insert, CandidateKind.Custom));
            }

            return candidates.Count == 0 ? null : candidates;
        }

        // Unexpected return type — caller will log once and fall back
        return null;
    }
}
