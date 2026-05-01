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

    // ── Wiring ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns <see cref="AliasBuiltIns.AliasExecutor"/> so that <c>alias.exec</c> calls in
    /// Stash code invoke the shell runner for template aliases.
    /// Must be called after <paramref name="shellRunner"/> is fully initialised.
    /// </summary>
    internal static void Wire(ShellRunner shellRunner, VirtualMachine vm)
    {
        AliasBuiltIns.AliasExecutor = (entry, args, _) =>
            ExecuteAlias(shellRunner, vm, entry, args);
    }

    // ── Dispatch ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a single alias entry.
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
        return entry.Kind == AliasRegistry.AliasKind.Function
            ? ExecuteFunctionAlias(runner, vm, entry, args)
            : ExecuteTemplateAlias(runner, vm, entry, args);
    }

    // ── Template alias ───────────────────────────────────────────────────────

    private static int ExecuteTemplateAlias(
        ShellRunner runner,
        VirtualMachine vm,
        AliasRegistry.AliasEntry entry,
        string[] args)
    {
        _expansionStack ??= new Stack<string>();

        // Cycle guard: detect if this alias is already being expanded on this thread.
        if (_expansionStack.Contains(entry.Name))
        {
            string chain = BuildChain(entry.Name);
            throw new RuntimeError(
                $"recursive alias expansion: {chain}",
                null,
                StashErrorTypes.AliasError);
        }

        // Depth guard: prevent runaway alias chains.
        if (_expansionStack.Count >= MaxChainDepth)
        {
            string chain = BuildChain(entry.Name);
            throw new RuntimeError(
                $"alias chain too deep (max {MaxChainDepth}): {chain}",
                null,
                StashErrorTypes.AliasError);
        }

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

        _expansionStack.Push(entry.Name);
        try
        {
            string expanded = AliasBuiltIns.ExpandTemplate(entry.Name, entry.TemplateBody!, args);
            // Re-feed expanded line through the same shell runner.
            // The runner will again check the alias registry (supporting alias chains)
            // and the _expansionStack on this thread prevents infinite loops.
            runner.Run(expanded);
            return vm.LastExitCode;
        }
        finally
        {
            _expansionStack.Pop();
        }
    }

    // ── Function alias ───────────────────────────────────────────────────────

    private static int ExecuteFunctionAlias(
        ShellRunner runner,
        VirtualMachine vm,
        AliasRegistry.AliasEntry entry,
        string[] args)
    {
        _expansionStack ??= new Stack<string>();

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
            StashValue[] stashArgs = Array.ConvertAll(args, StashValue.FromObj);
            // Invoke via the VM's interpreter context so closures run in the correct
            // global scope and LastExitCode is updated by any $(…) calls inside.
            vm.Context.InvokeCallbackDirect(entry.FunctionBody!, stashArgs);
            return vm.LastExitCode;
        }
        finally
        {
            _expansionStack.Pop();
        }
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
