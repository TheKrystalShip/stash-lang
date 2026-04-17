namespace Stash.Runtime.Protocols;

/// <summary>
/// Defines truthiness for boolean coercion contexts (if, while, &amp;&amp;, ||).
/// Types that don't implement this are always truthy (Lua convention).
/// </summary>
public interface IVMTruthiness
{
    /// <summary>
    /// Returns true if this value is falsy.
    /// </summary>
    bool VMIsFalsy { get; }
}
