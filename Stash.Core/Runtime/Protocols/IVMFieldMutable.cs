namespace Stash.Runtime.Protocols;

using Stash.Common;

/// <summary>
/// Supports writing named fields/properties via dot-assignment.
/// Separate from IVMFieldAccessible — many types support reading but not writing.
/// </summary>
public interface IVMFieldMutable
{
    /// <summary>
    /// Set the value of a named field.
    /// </summary>
    void VMSetField(string name, StashValue value, SourceSpan? span);
}
