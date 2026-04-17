namespace Stash.Runtime.Protocols;

using Stash.Common;

/// <summary>
/// Supports reading named fields/properties via the dot operator.
/// </summary>
public interface IVMFieldAccessible
{
    /// <summary>
    /// Get the value of a named field. Returns true if the field exists.
    /// Returning false lets the VM fall through to extension methods/UFCS.
    /// </summary>
    bool VMTryGetField(string name, out StashValue value, SourceSpan? span);
}
