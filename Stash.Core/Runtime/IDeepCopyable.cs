namespace Stash.Runtime;

/// <summary>
/// Interface for runtime values that support deep copying with snapshotted closures.
/// Used by RuntimeValues.DeepCopy() to clone callables without knowing their concrete type.
/// </summary>
public interface IDeepCopyable
{
    /// <summary>
    /// Creates a deep copy of this value with snapshotted closure chain.
    /// </summary>
    object DeepCopy();
}
