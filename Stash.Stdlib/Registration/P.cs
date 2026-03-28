namespace Stash.Stdlib.Registration;

using Stash.Stdlib.Models;

/// <summary>
/// Convenience factory for creating <see cref="BuiltInParam"/> instances.
/// Provides a terse syntax for use in builder definitions.
/// </summary>
public static class P
{
    /// <summary>Creates a parameter with name only.</summary>
    public static BuiltInParam Param(string name) => new(name);

    /// <summary>Creates a parameter with name and type hint.</summary>
    public static BuiltInParam Param(string name, string type) => new(name, type);
}
