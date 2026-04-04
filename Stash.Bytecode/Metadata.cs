namespace Stash.Bytecode;

using Stash.Runtime.Types;

/// <summary>Metadata for OP_STRUCTDECL. Stored in the constant pool.</summary>
internal sealed record StructMetadata(
    string Name,
    string[] Fields,
    string[] MethodNames,
    string[] InterfaceNames);

/// <summary>Metadata for OP_ENUMDECL. Stored in the constant pool.</summary>
internal sealed record EnumMetadata(
    string Name,
    string[] Members);

/// <summary>Metadata for OP_INTERFACEDECL. Stored in the constant pool.</summary>
internal sealed record InterfaceMetadata(
    string Name,
    InterfaceField[] Fields,
    InterfaceMethod[] Methods);

/// <summary>Metadata for OP_EXTEND. Stored in the constant pool.</summary>
internal sealed record ExtendMetadata(
    string TypeName,
    string[] MethodNames,
    bool IsBuiltIn);
