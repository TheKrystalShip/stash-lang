namespace Stash.Runtime.Errors;

/// <summary>
/// Registry of all built-in Stash error types, populated by the
/// <c>StashErrorRegistryGenerator</c> source generator. The generated partial
/// companion (<c>BuiltInErrorRegistry.g.cs</c>) emits the lookup tables;
/// this file holds any hand-written members.
/// </summary>
public static partial class BuiltInErrorRegistry
{
    // Generated partial companion emits:
    //   _byName, _byType, _metadata, ByName, ByType, Metadata,
    //   IsBuiltInName, TryGetName, NameOf.
}
