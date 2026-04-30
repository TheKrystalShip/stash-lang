using System;
using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Cli.Completion.Completers;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.BuiltIns;

namespace Stash.Cli.Completion;

/// <summary>
/// Populates the static delegate slots on <see cref="CompleteBuiltIns"/> so that
/// the <c>complete.*</c> stdlib functions delegate to the live
/// <see cref="CompletionEngine"/> and <see cref="CustomCompleterRegistry"/>.
/// </summary>
/// <remarks>
/// Call <see cref="Wire"/> once at REPL startup, after the VM and engine are ready,
/// before any user code (e.g. the RC file) runs.
/// </remarks>
internal static class CompletionWiring
{
    /// <summary>
    /// Wires the <see cref="CompleteBuiltIns"/> static delegate slots to the
    /// provided <paramref name="engine"/> and <paramref name="registry"/>.
    /// </summary>
    public static void Wire(CompletionEngine engine, CustomCompleterRegistry registry, VirtualMachine vm)
    {
        CompleteBuiltIns.RegisterHandler = (name, fn) => registry.Register(name, fn);

        CompleteBuiltIns.UnregisterHandler = name => registry.Unregister(name);

        CompleteBuiltIns.RegisteredHandler = () =>
        {
            var names = registry.RegisteredNames();
            var arr = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
                arr[i] = names[i];
            return arr;
        };

        CompleteBuiltIns.SuggestHandler = (line, cursor) =>
        {
            int c = cursor < 0 ? line.Length : cursor;
            CompletionResult result = engine.Complete(line, c);
            return BuildCompletionResultStruct(result);
        };

        CompleteBuiltIns.PathHelperHandler = ctxInst =>
        {
            CursorContext ctx = BuildCursorContextFromInstance(ctxInst);
            var deps = new CompletionDeps(vm, new Stash.Cli.Shell.PathExecutableCache(), registry, System.IO.TextWriter.Null);
            var pathCompleter = new PathCompleter();
            IReadOnlyList<Candidate> candidates = pathCompleter.Complete(ctx, deps);
            var paths = new string[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
                paths[i] = candidates[i].Insert;
            return paths;
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="CompletionResult"/> to a <c>CompletionResult</c>
    /// <see cref="StashInstance"/> (spec §9.2). Field names use snake_case to match the spec.
    /// </summary>
    internal static StashInstance BuildCompletionResultStruct(CompletionResult r)
    {
        var candidatesList = new List<StashValue>(r.Candidates.Count);
        foreach (var c in r.Candidates)
            candidatesList.Add(StashValue.FromObj(c.Insert));

        var fields = new Dictionary<string, StashValue>
        {
            ["replace_start"] = StashValue.FromInt(r.ReplaceStart),
            ["replace_end"]   = StashValue.FromInt(r.ReplaceEnd),
            ["candidates"]    = StashValue.FromObj(candidatesList),
            ["common_prefix"] = StashValue.FromObj(r.CommonPrefix),
        };

        return new StashInstance("CompletionResult", fields);
    }

    /// <summary>
    /// Extracts a <see cref="CursorContext"/> from a <c>CompletionContext</c>
    /// <see cref="StashInstance"/>. Used by the <c>complete.paths</c> helper.
    /// Field names use snake_case to match the spec.
    /// </summary>
    internal static CursorContext BuildCursorContextFromInstance(StashInstance ctx)
    {
        string modeStr = GetStringField(ctx, "mode", "shell");
        CompletionMode mode = modeStr switch
        {
            "stash"        => CompletionMode.Stash,
            "substitution" => CompletionMode.Substitution,
            _              => CompletionMode.Shell,
        };

        string current = GetStringField(ctx, "current", string.Empty);
        int position = GetIntField(ctx, "position", 1);

        // Build prior args from the CompletionContext.args array
        var priorArgs = new List<string>();
        try
        {
            StashValue argsVal = ctx.GetField("args", null);
            if (argsVal.ToObject() is List<StashValue> argList)
            {
                foreach (var sv in argList)
                {
                    if (sv.ToObject() is string s)
                        priorArgs.Add(s);
                }
            }
        }
        catch { /* missing or wrong type — leave empty */ }

        return new CursorContext(
            Mode: mode,
            ReplaceStart: 0,
            ReplaceEnd: current.Length,
            TokenText: current,
            InQuote: false,
            QuoteChar: '\0',
            InSubstitution: mode == CompletionMode.Substitution,
            PriorArgs: priorArgs);
    }

    private static string GetStringField(StashInstance inst, string field, string fallback)
    {
        try
        {
            object? val = inst.GetField(field, null).ToObject();
            return val is string s ? s : fallback;
        }
        catch { return fallback; }
    }

    private static int GetIntField(StashInstance inst, string field, int fallback)
    {
        try
        {
            StashValue sv = inst.GetField(field, null);
            if (sv.IsInt) return (int)sv.AsInt;
            return fallback;
        }
        catch { return fallback; }
    }
}
