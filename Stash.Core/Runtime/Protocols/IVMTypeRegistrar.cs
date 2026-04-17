namespace Stash.Runtime.Protocols;

using System;

/// <summary>
/// Registry for VM-level type information.
/// Allows external types to register type names for typeof/is dispatch.
/// </summary>
public interface IVMTypeRegistrar
{
    /// <summary>
    /// Register a type name mapping. When typeof encounters an object of the given
    /// CLR type, it returns the registered type name.
    /// </summary>
    void RegisterTypeName<T>(string vmTypeName) where T : class;

    /// <summary>
    /// Register a type check for the 'is' operator. When 'value is TypeName' is
    /// evaluated and TypeName matches, the predicate is called.
    /// </summary>
    void RegisterTypeCheck(string vmTypeName, Func<object, bool> predicate);
}
