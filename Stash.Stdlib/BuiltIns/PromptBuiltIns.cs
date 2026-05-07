namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>prompt</c> namespace built-in functions for REPL prompt customization.
/// </summary>
/// <remarks>
/// <para>
/// Provides primitives for registering a custom prompt renderer (<c>prompt.set</c>,
/// <c>prompt.reset</c>), a continuation prompt renderer (<c>prompt.setContinuation</c>,
/// <c>prompt.resetContinuation</c>), and utilities for inspecting the current prompt
/// context (<c>prompt.context</c>), rendering the prompt (<c>prompt.render</c>),
/// and managing a color palette (<c>prompt.palette</c>, <c>prompt.setPalette</c>).
/// </para>
/// <para>
/// State is stored in static fields scoped to the process. This is consistent with
/// how <c>sys</c> signal handlers are stored and works correctly for single-VM REPL
/// sessions. The renderer and REPL wiring are provided in later phases.
/// </para>
/// </remarks>
[StashNamespace]
public static partial class PromptBuiltIns
{
    // ---------------------------------------------------------------------------
    // Per-process prompt state (static, single-REPL assumption)
    // ---------------------------------------------------------------------------

    private static IStashCallable? _promptFn;
    private static IStashCallable? _continuationFn;
    private static StashValue _palette = StashValue.Null;
    private static long _lineNumber;

    /// <summary>
    /// Per-thread re-entry guard for <c>prompt.render()</c>. If a user prompt fn calls
    /// <c>prompt.render()</c> from within itself we abort with the literal fallback
    /// rather than recursing infinitely (spec \u00a715.1).
    /// </summary>
    [System.ThreadStatic]
    private static bool _renderingThread;

    // Theme and starter registries
    private static readonly System.Collections.Generic.Dictionary<string, StashValue> _themes =
        new(StringComparer.Ordinal);
    private static readonly System.Collections.Generic.Dictionary<string, IStashCallable> _starters =
        new(StringComparer.Ordinal);
    private static string _currentTheme = "";

    /// <summary>
    /// Optional handler for <c>prompt.resetBootstrap</c>. Set by <c>Stash.Cli</c> at
    /// startup to avoid a layering violation (Stdlib cannot reference Cli).
    /// </summary>
    public static Action? ResetBootstrapHandler { get; set; }

    /// <summary>
    /// Optional handler for probing git status. Set by <c>Stash.Cli</c> at startup.
    /// Receives the absolute working directory and returns a <c>PromptGit</c>
    /// <see cref="Stash.Runtime.Types.StashInstance"/>, or <c>null</c> when git
    /// is unavailable, not in a repo, or the probe timed out.
    /// </summary>
    public static Func<string, StashInstance?>? GitProbeHandler { get; set; }

    /// <summary>
    /// Optional resolver that looks up a top-level REPL global by name and returns it as a
    /// callable, or <c>null</c> if not present / not callable. Set by <c>Stash.Cli</c> at
    /// startup so that <c>prompt.render()</c> respects the convention discovery order
    /// (per spec §4.3): explicit <c>prompt.set</c> first, then the <c>prompt</c> convention
    /// global, then the built-in fallback. When unset (e.g. in unit tests without a REPL)
    /// only the explicit registration is consulted.
    /// </summary>
    public static Func<string, IStashCallable?>? ConventionFnResolver { get; set; }

    /// <summary>
    /// Set to <see langword="true"/> by <c>Stash.Cli</c> when shell mode is active for the
    /// current REPL session (per <c>--shell</c>, <c>STASH_SHELL=1</c>, or RC file presence).
    /// Drives the <c>mode</c> field of <see cref="BuildPromptContext"/>.
    /// </summary>
    public static bool ShellModeActive { get; set; }

    /// <summary>Registers a custom prompt render function. The function receives one argument (a PromptContext struct) and must return a string.</summary>
    /// <param name="fn">A callable that accepts a PromptContext and returns a string</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Set(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var callable = ValidateCallable(args, 0, "prompt.set");
        _promptFn = callable;
        return StashValue.Null;
    }

    /// <summary>Registers a custom continuation prompt render function shown when the user has entered a partial multi-line expression.</summary>
    /// <param name="fn">A callable that accepts a PromptContext and returns a string</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue SetContinuation(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var callable = ValidateCallable(args, 0, "prompt.setContinuation");
        _continuationFn = callable;
        return StashValue.Null;
    }

    /// <summary>Removes the custom prompt render function registered with prompt.set, reverting to the built-in default 'stash> ' prompt.</summary>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Reset(IInterpreterContext _, ReadOnlySpan<StashValue> _args)
    {
        _promptFn = null;
        return StashValue.Null;
    }

    /// <summary>Removes the custom continuation prompt render function registered with prompt.setContinuation, reverting to the built-in default.</summary>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue ResetContinuation(IInterpreterContext _, ReadOnlySpan<StashValue> _args)
    {
        _continuationFn = null;
        return StashValue.Null;
    }

    /// <summary>Builds and returns a PromptContext snapshot capturing the current working directory, user, hostname, time, last exit code, prompt line number, shell mode, and host color.</summary>
    /// <returns>A PromptContext struct instance</returns>
    [StashFn(Raw = true, ReturnType = "PromptContext")]
    private static StashValue Context(IInterpreterContext ctx, ReadOnlySpan<StashValue> _)
    {
        return StashValue.FromObj(BuildPromptContext(ctx));
    }

    /// <summary>Invokes the registered prompt render function with the current PromptContext and returns the resulting string. Falls back to 'stash> ' if no function is registered.</summary>
    /// <returns>The rendered prompt string</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Render(IInterpreterContext ctx, ReadOnlySpan<StashValue> _)
    {
        if (_renderingThread)
            return StashValue.FromObj("stash> ");
        IStashCallable? fn = _promptFn ?? ConventionFnResolver?.Invoke("prompt");
        if (fn is null)
            return StashValue.FromObj("stash> ");
        _renderingThread = true;
        try
        {
            var ctxInstance = BuildPromptContext(ctx);
            StashValue result = ctx.InvokeCallbackDirect(fn, [StashValue.FromObj(ctxInstance)]);
            if (result.ToObject() is string s)
                return StashValue.FromObj(s);
            string typeName = result.ToObject()?.GetType().Name ?? "null";
            throw new RuntimeError(
                $"prompt.render: user prompt fn returned {typeName}, expected string.",
                null,
                StashErrorTypes.TypeError);
        }
        finally
        {
            _renderingThread = false;
        }
    }

    /// <summary>Returns the currently registered prompt color palette (set via prompt.setPalette), or null if no palette has been configured.</summary>
    /// <returns>The palette value, or null</returns>
    [StashFn(Raw = true)]
    private static StashValue Palette(IInterpreterContext _, ReadOnlySpan<StashValue> _args)
    {
        return _palette;
    }

    /// <summary>Stores a palette value that the prompt render function can retrieve via prompt.palette(). No validation is performed.</summary>
    /// <param name="palette">Any value representing the color palette</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue SetPalette(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        _palette = args[0];
        return StashValue.Null;
    }

    /// <summary>Returns the path to the user-level prompt bootstrap directory (~/.config/stash/prompt).</summary>
    /// <returns>The absolute path to the prompt bootstrap directory</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue BootstrapDir(IInterpreterContext _, ReadOnlySpan<StashValue> _args)
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "stash", "prompt");
        return StashValue.FromObj(dir);
    }

    /// <summary>Re-extracts the prompt bootstrap scripts from embedded resources and reloads them into the REPL VM.</summary>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue ResetBootstrap(IInterpreterContext _, ReadOnlySpan<StashValue> _args)
    {
        ResetBootstrapHandler?.Invoke();
        return StashValue.Null;
    }

    /// <summary>Registers a Palette value under the given name in the theme registry.</summary>
    /// <param name="name">Theme name</param>
    /// <param name="palette">A Palette struct instance</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue ThemeRegister(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "prompt.themeRegister");
        _themes[name] = args[1];
        return StashValue.Null;
    }

    /// <summary>Activates a registered theme palette by name, making it available via prompt.palette(). Throws ValueError if the name is not registered.</summary>
    /// <param name="name">The registered theme name</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue ThemeUse(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "prompt.themeUse");
        if (!_themes.TryGetValue(name, out StashValue palette))
            throw new RuntimeError(
                $"prompt.themeUse: unknown theme '{name}'. Available: {string.Join(", ", _themes.Keys)}",
                null, StashErrorTypes.ValueError);
        _palette = palette;
        _currentTheme = name;
        return StashValue.Null;
    }

    /// <summary>Returns the name of the currently active theme, or an empty string if no theme has been activated.</summary>
    /// <returns>Theme name string</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue ThemeCurrent(IInterpreterContext _, ReadOnlySpan<StashValue> _args)
    {
        return StashValue.FromObj(_currentTheme);
    }

    /// <summary>Returns a sorted array of all registered theme names.</summary>
    /// <returns>string[]</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue ThemeList(IInterpreterContext _, ReadOnlySpan<StashValue> _args)
    {
        var keys = new System.Collections.Generic.List<string>(_themes.Keys);
        keys.Sort(StringComparer.Ordinal);
        var result = new System.Collections.Generic.List<StashValue>(keys.Count);
        foreach (string k in keys)
            result.Add(StashValue.FromObj(k));
        return StashValue.FromObj(result);
    }

    /// <summary>Registers a starter prompt function under the given name. The function must accept exactly one argument (PromptContext).</summary>
    /// <param name="name">Starter name</param>
    /// <param name="fn">A callable that accepts a PromptContext and returns a string</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue RegisterStarter(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "prompt.registerStarter");
        var callable = ValidateCallable(args, 1, "prompt.registerStarter");
        _starters[name] = callable;
        return StashValue.Null;
    }

    /// <summary>Activates a registered starter prompt function by name, making it the active prompt renderer. Throws ValueError if the name is not registered.</summary>
    /// <param name="name">The registered starter name</param>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue UseStarter(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        string name = SvArgs.String(args, 0, "prompt.useStarter");
        if (!_starters.TryGetValue(name, out IStashCallable? callable))
            throw new RuntimeError(
                $"prompt.useStarter: unknown starter '{name}'. Available: {string.Join(", ", _starters.Keys)}",
                null, StashErrorTypes.ValueError);
        _promptFn = callable;
        return StashValue.Null;
    }

    /// <summary>Returns a sorted array of all registered starter prompt names.</summary>
    /// <returns>string[]</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue ListStarters(IInterpreterContext _, ReadOnlySpan<StashValue> _args)
    {
        var keys = new System.Collections.Generic.List<string>(_starters.Keys);
        keys.Sort(StringComparer.Ordinal);
        var result = new System.Collections.Generic.List<StashValue>(keys.Count);
        foreach (string k in keys)
            result.Add(StashValue.FromObj(k));
        return StashValue.FromObj(result);
    }

    // ---------------------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the currently registered prompt render function, or <c>null</c> if none is set.
    /// Used by <c>PromptRenderer</c> in the REPL to invoke the user's prompt function.
    /// </summary>
    public static IStashCallable? GetRegisteredPromptFn() => _promptFn;

    /// <summary>
    /// Returns the currently registered continuation prompt render function, or <c>null</c> if none is set.
    /// </summary>
    public static IStashCallable? GetRegisteredContinuationFn() => _continuationFn;

    /// <summary>
    /// Clears the registered prompt function, reverting to the built-in default.
    /// Called by <c>PromptRenderer</c> after 5 consecutive failures.
    /// </summary>
    public static void ResetPromptFn() => _promptFn = null;

    /// <summary>
    /// Clears the registered continuation prompt function, reverting to the built-in default.
    /// Called by <c>PromptRenderer</c> after 5 consecutive continuation failures.
    /// </summary>
    public static void ResetContinuationFn() => _continuationFn = null;

    /// <summary>
    /// Resets all static prompt state to its initial values. For use in unit tests only.
    /// </summary>
    internal static void ResetAllForTesting()
    {
        _promptFn = null;
        _continuationFn = null;
        _palette = StashValue.Null;
        System.Threading.Interlocked.Exchange(ref _lineNumber, 0);
        _themes.Clear();
        _starters.Clear();
        _currentTheme = "";
    }

    /// <summary>
    /// Returns the name of the currently active theme. For use in unit tests only.
    /// </summary>
    internal static string GetCurrentThemeForTesting() => _currentTheme;

    /// <summary>
    /// Returns the currently active palette value. For use in unit tests only.
    /// </summary>
    internal static StashValue GetPaletteForTesting() => _palette;

    /// <summary>
    /// Returns a sorted list of registered theme names. For use in unit tests only.
    /// </summary>
    internal static List<string> GetRegisteredThemeNamesForTesting()
    {
        var keys = new List<string>(_themes.Keys);
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <summary>
    /// Returns a sorted list of registered starter names. For use in unit tests only.
    /// </summary>
    internal static List<string> GetRegisteredStarterNamesForTesting()
    {
        var keys = new List<string>(_starters.Keys);
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <summary>
    /// Validates that <paramref name="args"/>[<paramref name="index"/>] is a callable that
    /// accepts exactly one argument (or is variadic). Throws <c>TypeError</c> otherwise.
    /// </summary>
    private static IStashCallable ValidateCallable(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        if (index >= args.Length || args[index].ToObject() is not IStashCallable callable)
            throw new RuntimeError($"'{funcName}' requires a callable argument.", null, StashErrorTypes.TypeError);

        // Accept: variadic (Arity == -1), or MinArity <= 1 <= Arity
        bool isVariadic = callable.Arity == -1;
        bool acceptsOne = callable.MinArity <= 1 && callable.Arity >= 1;
        if (!isVariadic && !acceptsOne)
        {
            throw new RuntimeError(
                $"'{funcName}': the function must accept exactly 1 argument (got arity {callable.Arity}).",
                null,
                StashErrorTypes.TypeError);
        }

        return callable;
    }

    /// <summary>
    /// Builds a <c>PromptContext</c> struct instance capturing the current environment state.
    /// </summary>
    /// <param name="ctx">The current interpreter context (used for last exit code).</param>
    /// <param name="extraFields">Optional extra fields merged into the context dictionary (e.g. continuation metadata).</param>
    public static StashInstance BuildPromptContext(IInterpreterContext ctx, Dictionary<string, StashValue>? extraFields = null)
    {
        // Working directory with tilde compression
        string cwdAbs = Environment.CurrentDirectory;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string cwd = (!string.IsNullOrEmpty(home) && cwdAbs.StartsWith(home, StringComparison.Ordinal))
            ? "~" + cwdAbs.Substring(home.Length)
            : cwdAbs;

        // User
        string user = Environment.GetEnvironmentVariable("USER")
            ?? Environment.GetEnvironmentVariable("USERNAME")
            ?? "unknown";

        // Hostname (short form = first DNS label)
        string hostFull;
        try { hostFull = System.Net.Dns.GetHostName(); }
        catch { hostFull = Environment.MachineName; }
        string host = hostFull.Split('.')[0];

        // Unix time (float seconds)
        double time = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

        // Last exit code
        long lastExit = (long)ctx.GetLastExitCode();

        // Prompt line number (monotonically incrementing)
        long lineNumber = GetAndIncrementLineNumber();

        // Shell mode — reflects the active REPL session activation (--shell, STASH_SHELL=1,
        // or RC file presence). The CLI sets ShellModeActive at startup; falls back to the
        // STASH_SHELL env var when unset (e.g. in unit tests without a REPL).
        string mode = (ShellModeActive ||
                       Environment.GetEnvironmentVariable("STASH_SHELL") == "1")
            ? "shell" : "stash";

        // Host color — FNV-1a 32-bit hash, then map to 256-color ANSI fragment
        string hostColor = ComputeHostColor(host);

        // git — eagerly probed via the handler registered by Stash.Cli
        StashValue gitValue = StashValue.Null;
        try
        {
            StashInstance? gitInstance = GitProbeHandler?.Invoke(cwdAbs);
            if (gitInstance != null)
                gitValue = StashValue.FromObj(gitInstance);
        }
        catch
        {
            gitValue = StashValue.Null;
        }

        var dict = new Dictionary<string, StashValue>
        {
            ["cwd"]          = StashValue.FromObj(cwd),
            ["cwdAbsolute"]  = StashValue.FromObj(cwdAbs),
            ["user"]         = StashValue.FromObj(user),
            ["host"]         = StashValue.FromObj(host),
            ["hostFull"]     = StashValue.FromObj(hostFull),
            ["time"]         = StashValue.FromFloat(time),
            ["lastExitCode"] = StashValue.FromInt(lastExit),
            ["lineNumber"]   = StashValue.FromInt(lineNumber),
            ["mode"]         = StashValue.FromObj(mode),
            ["hostColor"]    = StashValue.FromObj(hostColor),
            ["git"]          = gitValue,
        };

        if (extraFields != null)
            foreach (var kv in extraFields)
                dict[kv.Key] = kv.Value;

        return new StashInstance("PromptContext", dict);
    }

    /// <summary>
    /// Reads, increments, and stores the per-process prompt line counter.
    /// Thread-safe via <see cref="System.Threading.Interlocked"/>.
    /// </summary>
    private static long GetAndIncrementLineNumber()
    {
        return System.Threading.Interlocked.Increment(ref _lineNumber);
    }

    /// <summary>
    /// Computes a stable 256-color ANSI SGR fragment for the given hostname using FNV-1a 32-bit hashing.
    /// Returns a string of the form <c>"38;5;N"</c> where N is in the 256-color extended palette.
    /// </summary>
    private static string ComputeHostColor(string host)
    {
        // FNV-1a 32-bit
        uint hash = 2166136261u;
        foreach (char c in host)
        {
            hash ^= (uint)c;
            hash *= 16777619u;
        }
        // Map to one of 6 base colors (each offset by 36 steps in the 256-color cube)
        // n = (hash % 6) * 36 + 17 keeps values in the colored region of the 256-color table
        int index = (int)(hash % 6) * 36 + 17;
        return "38;5;" + index.ToString();
    }
}
