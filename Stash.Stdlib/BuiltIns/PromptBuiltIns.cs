namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

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
public static class PromptBuiltIns
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

    /// <summary>
    /// Registers all <c>prompt</c> namespace functions.
    /// </summary>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("prompt");

        // prompt.set(fn) — Registers a custom prompt render function.
        ns.Function("set", [Param("fn", "function")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var callable = ValidateCallable(args, 0, "prompt.set");
            _promptFn = callable;
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Registers a custom prompt render function. The function receives one argument (a PromptContext struct) and must return a string. The function may be variadic (arity -1) or accept exactly one required parameter.\n@param fn A callable that accepts a PromptContext and returns a string\n@return null");

        // prompt.setContinuation(fn) — Registers a custom continuation prompt render function.
        ns.Function("setContinuation", [Param("fn", "function")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var callable = ValidateCallable(args, 0, "prompt.setContinuation");
            _continuationFn = callable;
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Registers a custom continuation prompt render function shown when the user has entered a partial multi-line expression. Receives one argument (a PromptContext struct) and must return a string.\n@param fn A callable that accepts a PromptContext and returns a string\n@return null");

        // prompt.reset() — Removes the registered prompt function, reverting to the default.
        ns.Function("reset", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            _promptFn = null;
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Removes the custom prompt render function registered with prompt.set, reverting to the built-in default 'stash> ' prompt.\n@return null");

        // prompt.resetContinuation() — Removes the registered continuation prompt function.
        ns.Function("resetContinuation", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            _continuationFn = null;
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Removes the custom continuation prompt render function registered with prompt.setContinuation, reverting to the built-in default.\n@return null");

        // prompt.context() — Builds and returns a PromptContext snapshot of the current environment.
        ns.Function("context", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> _) =>
        {
            return StashValue.FromObj(BuildPromptContext(ctx));
        },
            returnType: "PromptContext",
            documentation: "Builds and returns a PromptContext snapshot capturing the current working directory, user, hostname, time, last exit code, prompt line number, shell mode, and host color. The git field is null in Phase 1 (available after Phase 6).\n@return A PromptContext struct instance");

        // prompt.render() — Invokes the registered prompt function and returns the rendered string.
        // Per spec §8, prompt.render() is the explicit debugging path: it does NOT swallow
        // exceptions thrown by the user's prompt fn. Instead it propagates them so the user
        // can see what their fn is doing wrong. The REPL's own renderer (PromptRenderer) does
        // the catch-and-fallback dance — prompt.render() does not.
        ns.Function("render", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> _) =>
        {
            // Re-entry guard \u2014 prevents stack overflow if the prompt fn recurses into render().
            if (_renderingThread)
                return StashValue.FromObj("stash> ");

            // Discovery order (spec \u00a74.3): explicit registration > convention global > default.
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
        },
            returnType: "string",
            documentation: "Invokes the registered prompt render function with the current PromptContext and returns the resulting string. Falls back to 'stash> ' if no function is registered or if the function throws.\n@return The rendered prompt string");

        // prompt.palette() — Returns the currently registered prompt color palette, or null.
        ns.Function("palette", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            return _palette;
        },
            documentation: "Returns the currently registered prompt color palette (set via prompt.setPalette), or null if no palette has been configured.\n@return The palette value, or null");

        // prompt.setPalette(palette) — Stores a palette value for use by the prompt render function.
        ns.Function("setPalette", [Param("palette", "any")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            _palette = args[0];
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Stores a palette value that the prompt render function can retrieve via prompt.palette(). No validation is performed — the Stash-level prompt library is responsible for enforcing the palette shape.\n@param palette Any value representing the color palette\n@return null");

        // prompt.bootstrapDir() — Returns the path where prompt bootstrap scripts are discovered.
        ns.Function("bootstrapDir", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "stash", "prompt");
            return StashValue.FromObj(dir);
        },
            returnType: "string",
            documentation: "Returns the path to the user-level prompt bootstrap directory (~/.config/stash/prompt). Theme packages are loaded from subdirectories of this path.\n@return The absolute path to the prompt bootstrap directory");

        // prompt.resetBootstrap() — Re-extracts and reloads the bootstrap scripts.
        ns.Function("resetBootstrap", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            ResetBootstrapHandler?.Invoke();
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Re-extracts the prompt bootstrap scripts from embedded resources and reloads them into the REPL VM. Useful after a Stash upgrade or if the bootstrap directory was accidentally modified.\n@return null");

        // prompt.themeRegister(name, palette) — Stores a palette in the theme registry.
        ns.Function("themeRegister", [Param("name", "string"), Param("palette", "any")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            string name = SvArgs.String(args, 0, "prompt.themeRegister");
            _themes[name] = args[1];
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Registers a Palette value under the given name in the theme registry. Called by bundled theme files (e.g. themes/nord.stash). No shape validation is performed.\n@param name Theme name\n@param palette A Palette struct instance\n@return null");

        // prompt.themeUse(name) — Activates a registered theme by name.
        ns.Function("themeUse", [Param("name", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            string name = SvArgs.String(args, 0, "prompt.themeUse");
            if (!_themes.TryGetValue(name, out StashValue palette))
                throw new RuntimeError(
                    $"prompt.themeUse: unknown theme '{name}'. Available: {string.Join(", ", _themes.Keys)}",
                    null, StashErrorTypes.ValueError);
            _palette = palette;
            _currentTheme = name;
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Activates a registered theme palette by name, making it available via prompt.palette(). Throws ValueError if the name is not registered.\n@param name The registered theme name\n@return null");

        // prompt.themeCurrent() — Returns the name of the active theme.
        ns.Function("themeCurrent", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            return StashValue.FromObj(_currentTheme);
        },
            returnType: "string",
            documentation: "Returns the name of the currently active theme, or an empty string if no theme has been activated.\n@return Theme name string");

        // prompt.themeList() — Returns a sorted list of registered theme names.
        ns.Function("themeList", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            var keys = new System.Collections.Generic.List<string>(_themes.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = new System.Collections.Generic.List<StashValue>(keys.Count);
            foreach (string k in keys)
                result.Add(StashValue.FromObj(k));
            return StashValue.FromObj(result);
        },
            returnType: "array",
            documentation: "Returns a sorted array of all registered theme names.\n@return string[]");

        // prompt.registerStarter(name, fn) — Stores a starter prompt fn in the registry.
        ns.Function("registerStarter", [Param("name", "string"), Param("fn", "function")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            string name = SvArgs.String(args, 0, "prompt.registerStarter");
            var callable = ValidateCallable(args, 1, "prompt.registerStarter");
            _starters[name] = callable;
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Registers a starter prompt function under the given name. The function must accept exactly one argument (PromptContext). Called by bundled starter files.\n@param name Starter name\n@param fn A callable that accepts a PromptContext and returns a string\n@return null");

        // prompt.useStarter(name) — Activates a registered starter prompt fn.
        ns.Function("useStarter", [Param("name", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            string name = SvArgs.String(args, 0, "prompt.useStarter");
            if (!_starters.TryGetValue(name, out IStashCallable? callable))
                throw new RuntimeError(
                    $"prompt.useStarter: unknown starter '{name}'. Available: {string.Join(", ", _starters.Keys)}",
                    null, StashErrorTypes.ValueError);
            _promptFn = callable;
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Activates a registered starter prompt function by name, making it the active prompt renderer. Throws ValueError if the name is not registered.\n@param name The registered starter name\n@return null");

        // prompt.listStarters() — Returns a sorted list of registered starter names.
        ns.Function("listStarters", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            var keys = new System.Collections.Generic.List<string>(_starters.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = new System.Collections.Generic.List<StashValue>(keys.Count);
            foreach (string k in keys)
                result.Add(StashValue.FromObj(k));
            return StashValue.FromObj(result);
        },
            returnType: "array",
            documentation: "Returns a sorted array of all registered starter prompt names.\n@return string[]");

        return ns.Build();
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
