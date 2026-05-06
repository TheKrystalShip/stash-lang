namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>complete</c> namespace built-in functions for REPL tab completion.
/// </summary>
/// <remarks>
/// <para>
/// Provides the public API for custom completers: <c>complete.register</c>,
/// <c>complete.unregister</c>, <c>complete.registered</c>, <c>complete.suggest</c>,
/// and <c>complete.paths</c>.
/// </para>
/// <para>
/// State is bridged to <c>Stash.Cli</c> via static delegate slots.
/// <c>Stash.Stdlib</c> does not depend on <c>Stash.Cli</c>; the CLI sets these
/// slots at startup before any user code runs (see <c>CompletionWiring</c>).
/// When the slots are null (script mode or tests), calls are no-ops.
/// </para>
/// </remarks>
[StashNamespace]
public static partial class CompleteBuiltIns
{
    // ── Static delegate slots (populated by Stash.Cli at startup) ────────────

    /// <summary>
    /// Invoked by <c>complete.suggest</c>. Receives the line and cursor position,
    /// returns a <c>CompletionResult</c> <see cref="StashInstance"/>.
    /// </summary>
    public static Func<string, int, StashInstance>? SuggestHandler { get; set; }

    /// <summary>
    /// Invoked by <c>complete.paths</c>. Receives a <c>CompletionContext</c>
    /// <see cref="StashInstance"/> and returns an array of path strings.
    /// </summary>
    public static Func<StashInstance, string[]>? PathHelperHandler { get; set; }

    /// <summary>
    /// Invoked by <c>complete.register</c>. Receives the command name and the callable.
    /// </summary>
    public static Action<string, IStashCallable>? RegisterHandler { get; set; }

    /// <summary>
    /// Invoked by <c>complete.unregister</c>. Returns <c>true</c> if a completer was removed.
    /// </summary>
    public static Func<string, bool>? UnregisterHandler { get; set; }

    /// <summary>
    /// Invoked by <c>complete.registered</c>. Returns all registered command names.
    /// </summary>
    public static Func<string[]>? RegisteredHandler { get; set; }

    // ── Built-in functions ────────────────────────────────────────────────────

    /// <summary>Registers a Stash function as a custom tab completer for the given command name. The function receives a CompletionContext struct and must return an array of strings or an array of structs with 'display' and 'insert' fields.</summary>
    /// <param name="name">The command name to register a completer for</param>
    /// <param name="fn">A callable that accepts a CompletionContext and returns an array</param>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Register(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "complete.register");
        object? fnObj = args[1].ToObject();
        if (fnObj is not IStashCallable callable)
        {
            throw new RuntimeError(
                $"'complete.register' requires a callable as the second argument.",
                null,
                StashErrorTypes.TypeError);
        }

        RegisterHandler?.Invoke(name, callable);
        return StashValue.Null;
    }

    /// <summary>Removes the custom completer registered for the given command name.</summary>
    /// <param name="name">The command name whose completer should be removed</param>
    /// <returns>true if a completer was registered and removed, false otherwise</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Unregister(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "complete.unregister");
        if (UnregisterHandler is null)
            return StashValue.False;
        return StashValue.FromBool(UnregisterHandler(name));
    }

    /// <summary>Returns a lexicographically sorted array of all currently registered command names.</summary>
    /// <returns>array&lt;string&gt;</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Registered(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        if (RegisteredHandler is null)
            return StashValue.FromObj(new List<StashValue>());

        string[] names = RegisteredHandler();
        var result = new List<StashValue>(names.Length);
        foreach (string n in names)
            result.Add(StashValue.FromObj(n));
        return StashValue.FromObj(result);
    }

    /// <summary>Programmatically runs the tab-completion engine on the given line and cursor position. cursor = -1 means end-of-line. Returns a CompletionResult struct.</summary>
    /// <param name="line">The buffer string to complete</param>
    /// <param name="cursor">Cursor position (character/UTF-16 code unit index); -1 means end of line</param>
    /// <returns>CompletionResult struct with replace_start, replace_end, candidates, common_prefix</returns>
    [StashFn(Raw = true, ReturnType = "CompletionResult")]
    private static StashValue Suggest(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        string line = SvArgs.String(args, 0, "complete.suggest");
        int cursor = args.Length > 1 && !args[1].IsNull
            ? (int)SvArgs.Long(args, 1, "complete.suggest")
            : -1;

        if (cursor < 0)
            cursor = line.Length;

        if (SuggestHandler is not null)
            return StashValue.FromObj(SuggestHandler(line, cursor));

        return StashValue.FromObj(EmptyCompletionResult(cursor));
    }

    /// <summary>Helper for custom completers. Runs the default PathCompleter on the given CompletionContext and returns the candidate file path strings. Use this inside complete.register callbacks to augment file completion.</summary>
    /// <param name="ctx">A CompletionContext struct (passed to the completer function)</param>
    /// <returns>array&lt;string&gt; of file/directory paths</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Paths(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        object? ctxObj = args[0].ToObject();
        if (ctxObj is not StashInstance ctxInst || ctxInst.TypeName != "CompletionContext")
        {
            throw new RuntimeError(
                "'complete.paths' requires a CompletionContext struct argument.",
                null,
                StashErrorTypes.TypeError);
        }

        if (PathHelperHandler is null)
            return StashValue.FromObj(new List<StashValue>());

        string[] paths = PathHelperHandler(ctxInst);
        var result = new List<StashValue>(paths.Length);
        foreach (string p in paths)
            result.Add(StashValue.FromObj(p));
        return StashValue.FromObj(result);
    }

    // ── Testing helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Resets all static delegate slots to <c>null</c>. For use in unit tests only.
    /// </summary>
    public static void ResetAllForTesting()
    {
        SuggestHandler = null;
        PathHelperHandler = null;
        RegisterHandler = null;
        UnregisterHandler = null;
        RegisteredHandler = null;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static StashInstance EmptyCompletionResult(int cursor)
    {
        var fields = new Dictionary<string, StashValue>
        {
            ["replace_start"] = StashValue.FromInt(cursor),
            ["replace_end"]   = StashValue.FromInt(cursor),
            ["candidates"]    = StashValue.FromObj(new List<StashValue>()),
            ["common_prefix"] = StashValue.FromObj(string.Empty),
        };
        return new StashInstance("CompletionResult", fields);
    }
}
