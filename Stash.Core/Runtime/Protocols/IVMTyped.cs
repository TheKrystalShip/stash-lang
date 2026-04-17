namespace Stash.Runtime.Protocols;

/// <summary>
/// Provides type identity for the typeof operator and type checking.
/// </summary>
public interface IVMTyped
{
    /// <summary>
    /// Returns the type name for the typeof operator.
    /// Must be stable — same type always returns same name.
    /// </summary>
    string VMTypeName { get; }
}
