using System.Collections.Generic;
using Stash.Debugging;

namespace Stash.Bytecode;

/// <summary>
/// Adapter that exposes a VM call frame's local variables as an <see cref="IDebugScope"/>.
/// Built on-demand when the debugger requests variable inspection.
/// </summary>
internal sealed class VMDebugScope : IDebugScope
{
    private readonly KeyValuePair<string, object?>[] _bindings;
    private readonly IDebugScope? _enclosing;

    public VMDebugScope(KeyValuePair<string, object?>[] bindings, IDebugScope? enclosing)
    {
        _bindings = bindings;
        _enclosing = enclosing;
    }

    public IEnumerable<KeyValuePair<string, object?>> GetAllBindings() => _bindings;
    public IDebugScope? EnclosingScope => _enclosing;
}
