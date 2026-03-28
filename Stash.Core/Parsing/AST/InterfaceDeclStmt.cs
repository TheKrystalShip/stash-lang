namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A method signature within an interface declaration.
/// </summary>
/// <param name="Name">The method name token.</param>
/// <param name="Parameters">The list of parameter name tokens.</param>
/// <param name="ParameterTypes">The list of optional type hint tokens for each parameter. Each entry is <c>null</c> if no type hint was provided.</param>
/// <param name="ReturnType">The optional return type hint token, or <c>null</c>.</param>
public record InterfaceMethodSignature(Token Name, List<Token> Parameters, List<Token?> ParameterTypes, Token? ReturnType);

/// <summary>
/// An interface declaration: <c>interface Name { field1, method1(), ... }</c>
/// </summary>
/// <remarks>
/// Interfaces define contracts that structs can implement. They specify required fields
/// (bare identifiers with optional type hints) and method signatures (name + parameter count,
/// with optional parameter types and return type). Interfaces contain no method bodies.
/// </remarks>
public class InterfaceDeclStmt : Stmt
{
    /// <summary>Gets the interface name token.</summary>
    public Token Name { get; }
    /// <summary>Gets the list of required field name tokens.</summary>
    public List<Token> Fields { get; }
    /// <summary>Gets the list of optional type hint tokens for each required field. Each entry is <c>null</c> if no type was annotated.</summary>
    public List<Token?> FieldTypes { get; }
    /// <summary>Gets the list of method signatures declared in the interface.</summary>
    public List<InterfaceMethodSignature> Methods { get; }

    /// <summary>Initializes a new instance of <see cref="InterfaceDeclStmt"/>.</summary>
    /// <param name="name">The interface name token.</param>
    /// <param name="fields">The list of required field name tokens.</param>
    /// <param name="fieldTypes">The list of optional type hint tokens for each required field.</param>
    /// <param name="methods">The list of method signatures declared in the interface.</param>
    /// <param name="span">The source location of this declaration.</param>
    public InterfaceDeclStmt(Token name, List<Token> fields, List<Token?> fieldTypes, List<InterfaceMethodSignature> methods, SourceSpan span) : base(span)
    {
        Name = name;
        Fields = fields;
        FieldTypes = fieldTypes;
        Methods = methods;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitInterfaceDeclStmt(this);
}
