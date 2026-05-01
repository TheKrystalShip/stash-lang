using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Bytecode;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.BuiltIns;

namespace Stash.Cli.Shell;

/// <summary>
/// Executes aliases from the session-local alias registry.
/// Called by <see cref="ShellRunner"/> when a bare-word command matches a registered alias,
/// and also wired into <see cref="AliasBuiltIns.AliasExecutor"/> so that <c>alias.exec</c>
/// can trigger shell-line execution from stdlib code.
/// </summary>
/// <remarks>
/// <para>
/// The expansion stack is <see cref="ThreadStaticAttribute">thread-static</see> so that
/// alias chains that re-enter the shell runner on the same thread are tracked correctly
/// for cycle and depth detection. Cross-thread invocations (async/parallel) each get
/// their own stack.
/// </para>
/// <para>
/// Phase B: template aliases fully supported in single-stage and pipeline positions.
/// Function aliases supported in single-stage position only; function aliases in pipeline
/// stages fall through to PATH lookup (Phase E / future work will integrate them with the
/// VM pipeline machinery).
/// </para>
/// <para>
/// Phase E: hooks (confirm, before, after) are invoked around the body execution.
/// <see cref="ConfirmPrompter"/> is a static delegate slot that can be replaced in tests
/// to inject "y"/"n" responses without reading from stdin.
/// </para>
/// </remarks>
internal static class AliasDispatcher
{
    /// <summary>
    /// Per-thread expansion stack for cycle and depth detection.
    /// Elements are alias names currently being expanded on this thread.
    /// </summary>
    [ThreadStatic]
    private static Stack<string>? _expansionStack;

    /// <summary>Maximum alias chain depth before raising <see cref="StashErrorTypes.AliasError"/>.</summary>
    private const int MaxChainDepth = 32;

    /// <summary>
    /// Optional override for the confirm-prompt interaction. When non-null, this delegate
    /// is called instead of reading from <see cref="Console.In"/>.
    /// Receives the prompt text and returns <see langword="true"/> if the user accepts.
    /// Set this in tests to inject "y"/"n" without terminal input.
    /// </summary>
    public static Func<string, bool>? ConfirmPrompter { get; set; }

    // ── Wiring ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns <see cref="AliasBuiltIns.AliasExecutor"/> so that <c>alias.exec</c> calls in
    /// Stash code invoke the shell runner for template aliases.
    /// Also assigns <see cref="AliasBuiltIns.SaveHandler"/>, <see cref="AliasBuiltIns.LoadHandler"/>,
    /// and <see cref="AliasBuiltIns.RemoveSavedHandler"/> for Phase F persistence.
    /// Also registers the five built-in aliases (cd, pwd, exit, quit, history) into the
    /// VM's alias registry (Phase D).
    /// Must be called after <paramref name="shellRunner"/> is fully initialised.
    /// </summary>
    internal static void Wire(ShellRunner shellRunner, VirtualMachine vm)
    {
        AliasBuiltIns.AliasExecutor = (entry, args, _) =>
            ExecuteAlias(shellRunner, vm, entry, args);

        AliasBuiltIns.SaveHandler = (name) =>
            AliasPersistence.Save(vm, name);

        AliasBuiltIns.LoadHandler = (pathOverride) =>
            AliasPersistence.Load(vm, shellRunner, pathOverride);

        AliasBuiltIns.RemoveSavedHandler = (name) =>
            AliasPersistence.RemoveSaved(name);

        BuiltinAliases.RegisterBuiltins(vm);
    }

    // ── Dispatch ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a single alias entry.
    /// Implements the Phase E hook sequence: confirm → before → body → after.
    /// </summary>
    /// <param name="runner">The active <see cref="ShellRunner"/> for this REPL session.</param>
    /// <param name="vm">The active VM; <see cref="VirtualMachine.LastExitCode"/> is read after execution.</param>
    /// <param name="entry">The alias entry to execute.</param>
    /// <param name="args">String arguments parsed from the shell line.</param>
    /// <returns>Exit code of the executed command or function.</returns>
    internal static int ExecuteAlias(
        ShellRunner runner,
        VirtualMachine vm,
        AliasRegistry.AliasEntry entry,
        string[] args)
    {
        _expansionStack ??= new Stack<string>();

        // Cycle guard (spec §6.4 + §9.3): push BEFORE hooks so that a hook
        // calling the same alias also sees the entry on the stack.
        if (_expansionStack.Contains(entry.Name))
        {
            string chain = BuildChain(entry.Name);
            throw new RuntimeError(
                $"recursive alias expansion: {chain}",
                null,
                StashErrorTypes.AliasError);
        }

        if (_expansionStack.Count >= MaxChainDepth)
        {
            string chain = BuildChain(entry.Name);
            throw new RuntimeError(
                $"alias chain too deep (max {MaxChainDepth}): {chain}",
                null,
                StashErrorTypes.AliasError);
        }

        _expansionStack.Push(entry.Name);
        try
        {
            return ExecuteAliasCore(runner, vm, entry, args);
        }
        finally
        {
            _expansionStack.Pop();
        }
    }

    private static int ExecuteAliasCore(
        ShellRunner runner,
        VirtualMachine vm,
        AliasRegistry.AliasEntry entry,
        string[] args)
    {
        // ── Step 1: confirm hook ─────────────────────────────────────────────
        if (entry.Confirm is not null)
        {
            bool accepted = RunConfirmPrompt(entry.Confirm);
            if (!accepted)
            {
                vm.LastExitCode = 130;
                return 130;
            }
        }

        // ── Step 2: before hook ──────────────────────────────────────────────
        if (entry.Before is not null)
        {
            bool proceed;
            try
            {
                StashValue nameVal = StashValue.FromObj(entry.Name);
                StashValue argsVal = BuildArgsArray(args);
                StashValue result = vm.Context.InvokeCallbackDirect(entry.Before, [nameVal, argsVal]);
                proceed = RuntimeValues.IsTruthy(result.ToObject());
            }
            catch (RuntimeError re)
            {
                throw new RuntimeError(
                    $"hook 'before' for alias '{entry.Name}' threw: {re.Message}",
                    null,
                    StashErrorTypes.AliasError);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                throw new RuntimeError(
                    $"hook 'before' for alias '{entry.Name}' threw: {ex.Message}",
                    null,
                    StashErrorTypes.AliasError);
            }

            if (!proceed)
            {
                vm.LastExitCode = 1;
                return 1;
            }
        }

        // ── Step 3: body ─────────────────────────────────────────────────────
        int exitCode;
        try
        {
            exitCode = entry.Kind == AliasRegistry.AliasKind.Function
                ? ExecuteFunctionAliasBody(vm, entry, args)
                : ExecuteTemplateAliasBody(runner, vm, entry, args);
        }
        catch when (entry.After is null)
        {
            // No after hook; propagate immediately.
            throw;
        }
        catch (Exception bodyEx)
        {
            // Body threw but we have an after hook — run it with exit code 1, then re-throw.
            exitCode = 1;
            RunAfterHook(vm, entry, args, exitCode);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(bodyEx).Throw();
            throw; // unreachable
        }

        // ── Step 4: after hook ───────────────────────────────────────────────
        if (entry.After is not null)
        {
            RunAfterHook(vm, entry, args, exitCode);
        }

        return exitCode;
    }

    // ── Hook helpers ─────────────────────────────────────────────────────────

    private static bool RunConfirmPrompt(string promptText)
    {
        if (ConfirmPrompter is not null)
            return ConfirmPrompter(promptText);

        Console.Write(promptText);
        Console.Write(" [y/N] ");
        string? response = Console.ReadLine();
        return string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunAfterHook(
        VirtualMachine vm,
        AliasRegistry.AliasEntry entry,
        string[] args,
        int exitCode)
    {
        try
        {
            StashValue nameVal = StashValue.FromObj(entry.Name);
            StashValue argsVal = BuildArgsArray(args);
            StashValue codeVal = StashValue.FromObj((long)exitCode);
            vm.Context.InvokeCallbackDirect(entry.After!, [nameVal, argsVal, codeVal]);
        }
        catch (RuntimeError re)
        {
            throw new RuntimeError(
                $"hook 'after' for alias '{entry.Name}' threw: {re.Message}",
                null,
                StashErrorTypes.AliasError);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            throw new RuntimeError(
                $"hook 'after' for alias '{entry.Name}' threw: {ex.Message}",
                null,
                StashErrorTypes.AliasError);
        }
    }

    /// <summary>
    /// Converts a <c>string[]</c> into a Stash array value (<c>List&lt;StashValue&gt;</c>).
    /// </summary>
    private static StashValue BuildArgsArray(string[] args)
    {
        var list = new System.Collections.Generic.List<StashValue>(args.Length);
        foreach (string a in args)
            list.Add(StashValue.FromObj(a));
        return StashValue.FromObj(list);
    }

    // ── Template alias ───────────────────────────────────────────────────────

    private static int ExecuteTemplateAliasBody(
        ShellRunner runner,
        VirtualMachine vm,
        AliasRegistry.AliasEntry entry,
        string[] args)
    {
        // Strict-args check (spec §15): if the body has no arg placeholder and
        // the caller supplied arguments, raise an error rather than silently
        // discarding them (no implicit magic per spec §2).
        if (args.Length > 0 && !AliasBuiltIns.HasArgPlaceholder(entry.TemplateBody!))
        {
            throw new RuntimeError(
                $"alias '{entry.Name}' takes no arguments; " +
                "use a template with ${args} placeholder to forward arguments",
                null,
                StashErrorTypes.AliasError);
        }

        string expanded = AliasBuiltIns.ExpandTemplate(entry.Name, entry.TemplateBody!, args);
        // Re-feed expanded line through the same shell runner.
        // The runner will again check the alias registry (supporting alias chains).
        // Cycle detection is handled by the _expansionStack at the ExecuteAlias level.
        runner.Run(expanded);
        return vm.LastExitCode;
    }

    // ── Function alias ───────────────────────────────────────────────────────

    private static int ExecuteFunctionAliasBody(
        VirtualMachine vm,
        AliasRegistry.AliasEntry entry,
        string[] args)
    {
        StashValue[] stashArgs = Array.ConvertAll(args, StashValue.FromObj);
        // Invoke via the VM's interpreter context so closures run in the correct
        // global scope and LastExitCode is updated by any $(…) calls inside.
        vm.Context.InvokeCallbackDirect(entry.FunctionBody!, stashArgs);
        return vm.LastExitCode;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a human-readable expansion chain string for error messages,
    /// e.g. <c>"a → b → a"</c>.
    /// </summary>
    private static string BuildChain(string newName)
    {
        // _expansionStack enumerates LIFO (top to bottom).  Reverse() gives
        // chronological (bottom to top = push order) for a readable chain.
        return string.Join(" → ", _expansionStack!.Reverse().Append(newName));
    }
}
