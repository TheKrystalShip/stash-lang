namespace Stash.Runtime.Protocols;

/// <summary>
/// Defines custom equality semantics for the == operator.
/// Types that don't implement this use reference equality (the default for Obj-tagged values).
/// This is for the == operator only — StashValue.Equals() (used by C# collections) remains unchanged.
/// </summary>
public interface IVMEquatable
{
    /// <summary>
    /// Returns true if this value equals the other value.
    /// Only called when both values have the same Obj tag.
    /// </summary>
    bool VMEquals(StashValue other);
}
