namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A struct declaration: <c>struct Name { field1, field2, ... }</c>
/// </summary>
/// <remarks>
/// Structs are nominal types with named fields and optional methods. Fields may have optional type hints
/// (not enforced at runtime). Methods declared inside the struct receive an implicit <c>self</c> parameter
/// bound at call time. Methods are stored on the struct template (<c>StashStruct.Methods</c>), not per-instance.
/// </remarks>
public class StructDeclStmt : Stmt
{
    /// <summary>Gets the struct name token.</summary>
    public Token Name { get; }
    /// <summary>Gets the list of field name tokens.</summary>
    public List<Token> Fields { get; }
    /// <summary>Gets the list of optional type hints for each field. Each entry is <c>null</c> if no type was annotated.</summary>
    public List<TypeHint?> FieldTypes { get; }
    /// <summary>Gets the list of method declarations defined inside the struct body.</summary>
    public List<FnDeclStmt> Methods { get; }
    /// <summary>Gets the list of interface name tokens this struct declares conformance with.</summary>
    public List<Token> Interfaces { get; }

    /// <summary>Initializes a new instance of <see cref="StructDeclStmt"/>.</summary>
    /// <param name="name">The struct name token.</param>
    /// <param name="fields">The list of field name tokens.</param>
    /// <param name="fieldTypes">The list of optional type hints for each field.</param>
    /// <param name="methods">The list of method declarations defined inside the struct body.</param>
    /// <param name="interfaces">The list of interface name tokens this struct declares conformance with.</param>
    /// <param name="span">The source location of this declaration.</param>
    public StructDeclStmt(Token name, List<Token> fields, List<TypeHint?> fieldTypes, List<FnDeclStmt> methods, List<Token> interfaces, SourceSpan span) : base(span, StmtType.StructDecl)
    {
        Name = name;
        Fields = fields;
        FieldTypes = fieldTypes;
        Methods = methods;
        Interfaces = interfaces;
    }

    /// <summary>Initializes a new instance of <see cref="StructDeclStmt"/> with no interface declarations.</summary>
    /// <param name="name">The struct name token.</param>
    /// <param name="fields">The list of field name tokens.</param>
    /// <param name="fieldTypes">The list of optional type hints for each field.</param>
    /// <param name="methods">The list of method declarations defined inside the struct body.</param>
    /// <param name="span">The source location of this declaration.</param>
    public StructDeclStmt(Token name, List<Token> fields, List<TypeHint?> fieldTypes, List<FnDeclStmt> methods, SourceSpan span)
        : this(name, fields, fieldTypes, methods, new List<Token>(), span) { }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitStructDeclStmt(this);
}
