namespace Stash.Hosting;

using Stash.Bytecode;

/// <summary>
/// An opaque wrapper around a compiled Stash script.
/// Produced by <see cref="IStashHost.CompileAsync"/> and consumed by
/// <see cref="IStashHost.RunAsync(CompiledScript, System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// A <see cref="CompiledScript"/> is not bound to a specific host instance; however,
/// running it through a different host than the one that created it is unspecified
/// and may fail due to resolver state embedded in the underlying script. The v1
/// contract is: use a <see cref="CompiledScript"/> with the host that created it.
/// </remarks>
public sealed class CompiledScript
{
    internal StashScript Inner { get; }

    internal CompiledScript(StashScript inner)
    {
        Inner = inner;
    }
}
