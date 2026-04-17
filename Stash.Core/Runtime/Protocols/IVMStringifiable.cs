namespace Stash.Runtime.Protocols;

/// <summary>
/// Defines how a value is converted to string for interpolation, println, etc.
/// Types that don't implement this fall back to object.ToString().
/// </summary>
public interface IVMStringifiable
{
    /// <summary>
    /// Convert this value to its string representation.
    /// </summary>
    string VMToString();
}
