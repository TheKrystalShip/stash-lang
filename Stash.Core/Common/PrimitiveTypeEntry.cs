namespace Stash.Common;

/// <summary>
/// A row in the Stash primitive-types registry, carrying the type's name, tooling description,
/// and capability flags. Replaces the anonymous <c>(string Name, string Description)</c> tuple
/// used previously in <see cref="PrimitiveTypes"/>.
/// </summary>
public sealed record PrimitiveTypeEntry(
    string Name,
    string Description,
    PrimitiveCapability Caps);
