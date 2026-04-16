namespace Stash.Bytecode;

using Stash.Runtime.Types;

/// <summary>Metadata for OP_STRUCTDECL. Stored in the constant pool.</summary>
public sealed record StructMetadata(
    string Name,
    string[] Fields,
    string[] MethodNames,
    string[] InterfaceNames);

/// <summary>Metadata for OP_ENUMDECL. Stored in the constant pool.</summary>
public sealed record EnumMetadata(
    string Name,
    string[] Members);

/// <summary>Metadata for OP_INTERFACEDECL. Stored in the constant pool.</summary>
public sealed record InterfaceMetadata(
    string Name,
    InterfaceField[] Fields,
    InterfaceMethod[] Methods);

/// <summary>Metadata for OP_EXTEND. Stored in the constant pool.</summary>
public sealed record ExtendMetadata(
    string TypeName,
    string[] MethodNames,
    bool IsBuiltIn);

/// <summary>Metadata for OP_COMMAND. Stored in the constant pool.</summary>
public sealed record CommandMetadata(int PartCount, bool IsPassthrough, bool IsStrict);

/// <summary>Metadata for OP_IMPORT. Stored in the constant pool.</summary>
public sealed record ImportMetadata(string[] Names);

/// <summary>Metadata for OP_IMPORTAS. Stored in the constant pool.</summary>
public sealed record ImportAsMetadata(string AliasName);

/// <summary>Metadata for OP_DESTRUCTURE. Stored in the constant pool.</summary>
public sealed record DestructureMetadata(string Kind, string[] Names, string? RestName, bool IsConst);

/// <summary>Metadata for OP_RETRY. Stored in the constant pool.</summary>
public sealed record RetryMetadata(int OptionCount, bool HasUntilClause, bool HasOnRetryClause, bool OnRetryIsReference);

/// <summary>
/// Metadata for OP_NEWSTRUCT. Stored in the constant pool.
/// When HasTypeReg is false, TypeName names the struct (global lookup).
/// When HasTypeReg is true, TypeName is empty and R(A+1) holds the struct type; field values start at R(A+2).
/// </summary>
public sealed record StructInitMetadata(
    string TypeName,
    bool HasTypeReg,
    string[] FieldNames);
