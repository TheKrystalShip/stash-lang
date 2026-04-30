using Stash.Cli.Shell;
using Stash.Bytecode;

namespace Stash.Cli.Completion;

/// <summary>
/// Dependency bundle passed to every completer's <c>Complete</c> method.
/// Keeps completers testable without owning global state.
/// </summary>
internal sealed record CompletionDeps(
    VirtualMachine Vm,
    PathExecutableCache PathCache,
    CustomCompleterRegistry CustomCompleters);
