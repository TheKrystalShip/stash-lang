namespace Stash.Cli.Repl;

using System;
using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.BuiltIns;

/// <summary>
/// Renders the REPL prompt by invoking the user-registered prompt function (if any),
/// falling back to the built-in default <c>"stash> "</c> on error or when no function
/// is registered.
/// </summary>
internal static class PromptRenderer
{
    // ── OSC 133 shell-integration markers ──────────────────────────────────
    // Wrapped in \x01..\x02 (readline zero-width region) so PromptWidthCalculator
    // strips them when measuring display width.
    private const string OscPromptStart = "\x01\x1b]133;A\x07\x02";
    private const string OscPromptEnd   = "\x01\x1b]133;B\x07\x02";

    /// <summary>
    /// Whether to emit OSC 133 shell-integration markers. Computed once at type-init.
    /// Disabled when stdout is redirected, TERM is dumb/linux/screen*, or
    /// <c>STASH_NO_OSC133=1</c>.
    /// </summary>
    private static readonly bool _emitOsc133 = ComputeOsc133Eligible();

    // ── Primary prompt state ────────────────────────────────────────────────
    private static bool _warningEmitted;
    private static int _consecutiveFailures;
    private static bool _rendering; // re-entry guard

    // ── Continuation prompt state ───────────────────────────────────────────
    private static bool _continuationWarningEmitted;
    private static int _continuationConsecutiveFailures;

    // ── OSC 133 eligibility ─────────────────────────────────────────────────

    private static bool ComputeOsc133Eligible()
    {
        if (Console.IsOutputRedirected)
            return false;
        if (Environment.GetEnvironmentVariable("STASH_NO_OSC133") == "1")
            return false;
        string? term = Environment.GetEnvironmentVariable("TERM");
        if (term is "dumb" or "linux")
            return false;
        if (term?.StartsWith("screen", StringComparison.Ordinal) == true)
            return false;
        return true;
    }

    // ── OSC 133 C/D helpers (written directly to Console.Out in the run-loop) ─

    /// <summary>
    /// Writes the OSC 133 "command start" marker (<c>C</c>) to <c>Console.Out</c>.
    /// Call this immediately before evaluating a REPL line.
    /// </summary>
    public static void WriteCommandStart()
    {
        if (_emitOsc133)
            Console.Out.Write("\x1b]133;C\x07");
    }

    /// <summary>
    /// Writes the OSC 133 "command end" marker (<c>D;<exitCode></c>) to <c>Console.Out</c>.
    /// Call this immediately after evaluating a REPL line.
    /// </summary>
    public static void WriteCommandEnd(int exitCode)
    {
        if (_emitOsc133)
            Console.Out.Write($"\x1b]133;D;{exitCode}\x07");
    }

    // ── Primary prompt rendering ────────────────────────────────────────────

    /// <summary>
    /// Renders the primary prompt string. Invokes the registered prompt function if one is
    /// set; otherwise returns <c>"stash> "</c>. Catches all exceptions and falls back to the
    /// default, emitting a one-shot warning to <c>stderr</c>. Includes OSC 133 A/B markers
    /// when eligible.
    /// </summary>
    public static string Render(VirtualMachine vm)
    {
        // Re-entry guard — prevents stack overflow if the prompt fn calls prompt.render().
        if (_rendering)
            return WrapOsc("stash> ");

        IStashCallable? fn = PromptBuiltIns.GetRegisteredPromptFn()
            ?? GetConventionFn(vm, "prompt");

        if (fn is null)
            return WrapOsc("stash> ");

        _rendering = true;
        try
        {
            IInterpreterContext ctx = vm.Context;
            StashInstance ctxInstance = PromptBuiltIns.BuildPromptContext(ctx);
            StashValue result = ctx.InvokeCallbackDirect(fn, [StashValue.FromObj(ctxInstance)]);

            if (result.ToObject() is string s)
            {
                _consecutiveFailures = 0;
                return WrapOsc(s);
            }

            // Non-string return value.
            string typeName = result.ToObject()?.GetType().Name ?? "null";
            if (!_warningEmitted)
            {
                _warningEmitted = true;
                Console.Error.WriteLine($"prompt: user prompt fn returned {typeName}, expected string — falling back.");
            }

            _consecutiveFailures++;
            CheckAutoReset();
            return WrapOsc("stash> ");
        }
        catch (RuntimeError ex)
        {
            if (!_warningEmitted)
            {
                _warningEmitted = true;
                Console.Error.WriteLine($"prompt: error in user prompt fn — falling back to default. RuntimeError: {ex.Message}");
            }

            _consecutiveFailures++;
            CheckAutoReset();
            return WrapOsc("stash> ");
        }
        catch (Exception ex)
        {
            if (!_warningEmitted)
            {
                _warningEmitted = true;
                Console.Error.WriteLine($"prompt: error in user prompt fn — falling back to default. {ex.GetType().Name}: {ex.Message}");
            }

            _consecutiveFailures++;
            CheckAutoReset();
            return WrapOsc("stash> ");
        }
        finally
        {
            _rendering = false;
        }
    }

    // ── Continuation prompt rendering ───────────────────────────────────────

    /// <summary>
    /// Returns the continuation prompt string. Invokes the registered continuation function
    /// (or the <c>prompt_continuation</c> convention global) if one is set; otherwise returns
    /// <c>"... "</c>. The <paramref name="depth"/> parameter is 1 for the first continuation
    /// line and increments for each subsequent line.
    /// </summary>
    public static string Continuation(VirtualMachine vm, int depth = 1)
    {
        IStashCallable? fn = PromptBuiltIns.GetRegisteredContinuationFn()
            ?? GetConventionFn(vm, "prompt_continuation");

        if (fn is null)
            return "... ";

        try
        {
            IInterpreterContext ctx = vm.Context;
            var extraFields = new Dictionary<string, StashValue>
            {
                ["continuationDepth"]  = StashValue.FromInt(depth),
                ["continuationReason"] = StashValue.FromObj("open"),
            };
            StashInstance ctxInstance = PromptBuiltIns.BuildPromptContext(ctx, extraFields);
            StashValue result = ctx.InvokeCallbackDirect(fn, [StashValue.FromObj(ctxInstance)]);

            if (result.ToObject() is string s)
            {
                _continuationConsecutiveFailures = 0;
                return s;
            }

            // Non-string return value.
            string typeName = result.ToObject()?.GetType().Name ?? "null";
            if (!_continuationWarningEmitted)
            {
                _continuationWarningEmitted = true;
                Console.Error.WriteLine($"prompt: continuation fn returned {typeName}, expected string — falling back.");
            }

            _continuationConsecutiveFailures++;
            CheckContinuationAutoReset();
            return "... ";
        }
        catch (RuntimeError ex)
        {
            if (!_continuationWarningEmitted)
            {
                _continuationWarningEmitted = true;
                Console.Error.WriteLine($"prompt: error in continuation fn — falling back. RuntimeError: {ex.Message}");
            }

            _continuationConsecutiveFailures++;
            CheckContinuationAutoReset();
            return "... ";
        }
        catch (Exception ex)
        {
            if (!_continuationWarningEmitted)
            {
                _continuationWarningEmitted = true;
                Console.Error.WriteLine($"prompt: error in continuation fn — falling back. {ex.GetType().Name}: {ex.Message}");
            }

            _continuationConsecutiveFailures++;
            CheckContinuationAutoReset();
            return "... ";
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Looks up a convention global (e.g. <c>"prompt"</c> or <c>"prompt_continuation"</c>)
    /// in the VM's globals and returns it as a callable, or <c>null</c> if not found / not callable.
    /// </summary>
    private static IStashCallable? GetConventionFn(VirtualMachine vm, string globalName)
    {
        if (!vm.HasReplGlobal(globalName))
            return null;
        if (vm.Globals.TryGetValue(globalName, out StashValue val) && val.ToObject() is IStashCallable callable)
            return callable;
        return null;
    }

    /// <summary>Wraps <paramref name="prompt"/> with OSC 133 A/B markers when eligible.</summary>
    private static string WrapOsc(string prompt)
    {
        if (!_emitOsc133)
            return prompt;
        return OscPromptStart + prompt + OscPromptEnd;
    }

    /// <summary>
    /// Checks whether the primary prompt has hit 5 consecutive failures and, if so,
    /// auto-resets the registered function back to the default.
    /// </summary>
    private static void CheckAutoReset()
    {
        if (_consecutiveFailures < 5)
            return;
        PromptBuiltIns.ResetPromptFn();
        _consecutiveFailures = 0;
        _warningEmitted = false;
        Console.Error.WriteLine("prompt: 5 consecutive failures — auto-reset to default.");
    }

    /// <summary>
    /// Checks whether the continuation prompt has hit 5 consecutive failures and, if so,
    /// auto-resets the registered continuation function back to the default.
    /// </summary>
    private static void CheckContinuationAutoReset()
    {
        if (_continuationConsecutiveFailures < 5)
            return;
        PromptBuiltIns.ResetContinuationFn();
        _continuationConsecutiveFailures = 0;
        _continuationWarningEmitted = false;
        Console.Error.WriteLine("prompt: 5 consecutive continuation failures — auto-reset to default.");
    }

    /// <summary>
    /// Resets all static renderer state to its initial values. For use in unit tests only.
    /// </summary>
    internal static void ResetStateForTesting()
    {
        _warningEmitted = false;
        _consecutiveFailures = 0;
        _rendering = false;
        _continuationWarningEmitted = false;
        _continuationConsecutiveFailures = 0;
    }

    /// <summary>
    /// Sets the re-entry rendering flag to the specified value. For use in unit tests only.
    /// </summary>
    internal static void SetRenderingForTesting(bool value) => _rendering = value;
}
