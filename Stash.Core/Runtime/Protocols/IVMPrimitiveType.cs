namespace Stash.Runtime.Protocols;

/// <summary>
/// Marks a runtime type as a Stash-visible opaque primitive whose name and
/// description should appear in PrimitiveTypes.Names and PrimitiveTypes.Descriptions.
/// Implemented as a static abstract pair so the registry can read them without
/// instantiating the type.
/// </summary>
public interface IVMPrimitiveType
{
    static abstract string PrimitiveTypeName { get; }
    static abstract string PrimitiveTypeDescription { get; }
}
