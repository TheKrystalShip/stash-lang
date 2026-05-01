namespace Stash.Cli.Shell;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Runtime;

/// <summary>
/// Registers the five built-in shell aliases (<c>cd</c>, <c>pwd</c>, <c>exit</c>,
/// <c>quit</c>, <c>history</c>) into the VM's alias registry at startup.
///
/// <para>
/// Each built-in is registered as a <see cref="AliasRegistry.AliasKind.Function"/> alias
/// whose body is a <see cref="BuiltInFunction"/> that delegates to the existing
/// <see cref="ShellSugarDesugarer"/> helpers. This preserves all error messages,
/// argument-validation semantics, and platform-specific behaviour from Phase B/C while
/// making the commands visible via <c>alias.list()</c> / <c>alias.get("cd")</c> and
/// overridable with <c>alias.define(..., AliasOptions { override: true })</c>.
/// </para>
///
/// <para>
/// Called once from <see cref="AliasDispatcher.Wire"/> before the REPL loop starts.
/// If the registry already contains a Disabled entry for a name (i.e. the user called
/// <c>unalias --force</c> in a prior session — impossible since the registry is
/// in-process, but during tests <c>RegisterBuiltins</c> may be called again after
/// force-disabling), the <see cref="AliasRegistry.Define"/> overwrite clears the
/// Disabled flag because it replaces the entry wholesale with a fresh one.
/// </para>
/// </summary>
internal static class BuiltinAliases
{
    /// <summary>
    /// Registers all five built-in aliases into <paramref name="vm"/>'s alias registry.
    /// Existing built-in entries are overwritten (re-registration is always allowed for
    /// <c>Source == Builtin</c> entries — see <see cref="AliasRegistry.Define"/>).
    /// </summary>
    internal static void RegisterBuiltins(VirtualMachine vm)
    {
        Register(vm, "cd",      "change directory",       MakeCd(vm));
        Register(vm, "pwd",     "print working directory", MakePwd(vm));
        Register(vm, "exit",    "exit the shell",          MakeExit("exit", vm));
        Register(vm, "quit",    "alias for exit",          MakeExit("quit", vm));
        Register(vm, "history", "show command history",    MakeHistory(vm));
    }

    // ── Per-command body factories ────────────────────────────────────────────

    private static BuiltInFunction MakeCd(VirtualMachine vm) =>
        new("cd", arity: 0, (_, stashArgs) =>
        {
            string[] args = ExtractStringArgs(stashArgs);
            string src = ShellSugarDesugarer.DesugarCd(args);
            ShellRunner.EvaluateSource(src, vm);
            // Built-in shell commands that run purely via the VM succeed with exit 0.
            // Replicates the explicit `_ctx.Vm.LastExitCode = 0` from the old sugar path.
            vm.LastExitCode = 0;
            return StashValue.Null;
        });

    private static BuiltInFunction MakePwd(VirtualMachine vm) =>
        new("pwd", arity: 0, (_, stashArgs) =>
        {
            string[] args = ExtractStringArgs(stashArgs);
            string src = ShellSugarDesugarer.DesugarPwd(args);
            ShellRunner.EvaluateSource(src, vm);
            vm.LastExitCode = 0;
            return StashValue.Null;
        });

    private static BuiltInFunction MakeExit(string program, VirtualMachine vm) =>
        new(program, arity: 0, (_, stashArgs) =>
        {
            string[] args = ExtractStringArgs(stashArgs);
            string src = ShellSugarDesugarer.DesugarExit(program, args);
            // EvaluateSource throws ExitException — LastExitCode is irrelevant.
            ShellRunner.EvaluateSource(src, vm);
            vm.LastExitCode = 0;
            return StashValue.Null;
        });

    private static BuiltInFunction MakeHistory(VirtualMachine vm) =>
        new("history", arity: 0, (_, stashArgs) =>
        {
            string[] args = ExtractStringArgs(stashArgs);
            string src = ShellSugarDesugarer.DesugarHistory(args);
            ShellRunner.EvaluateSource(src, vm);
            vm.LastExitCode = 0;
            return StashValue.Null;
        });

    // ── Registry helper ───────────────────────────────────────────────────────

    private static void Register(
        VirtualMachine vm,
        string name,
        string description,
        BuiltInFunction body)
    {
        vm.AliasRegistry.Define(new AliasRegistry.AliasEntry
        {
            Name        = name,
            Kind        = AliasRegistry.AliasKind.Function,
            FunctionBody = body,
            Source      = AliasRegistry.AliasSource.Builtin,
            Description = description,
        });
    }

    // ── Argument conversion ───────────────────────────────────────────────────

    /// <summary>
    /// Converts the <see cref="StashValue"/> span received by the function body back to
    /// a plain <c>string[]</c>. Each element was created by
    /// <see cref="StashValue.FromObj(object?)"/> from a <c>string</c> in
    /// <see cref="AliasDispatcher"/>, so the conversion is always safe.
    /// </summary>
    private static string[] ExtractStringArgs(ReadOnlySpan<StashValue> stashArgs)
    {
        var result = new string[stashArgs.Length];
        for (int i = 0; i < stashArgs.Length; i++)
        {
            StashValue sv = stashArgs[i];
            result[i] = (sv.IsObj && sv.AsObj is string s) ? s : sv.ToString() ?? string.Empty;
        }
        return result;
    }
}
