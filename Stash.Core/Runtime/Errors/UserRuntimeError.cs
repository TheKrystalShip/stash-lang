namespace Stash.Runtime.Errors;

using Stash.Common;

/// <summary>
/// Carries an arbitrary user-supplied error type name from Stash code
/// (e.g. <c>throw { type: "MyError", message: "..." }</c>).
/// Never raised from built-in C# code.
/// </summary>
public sealed class UserRuntimeError : RuntimeError
{
    /// <summary>
    /// The user-supplied type name. This is the <b>only</b> error class where a string name is
    /// stored in a field, because the name comes from runtime data (a Stash dict literal),
    /// not from a C# class identity. The C#→Stash conversion uses this directly instead of
    /// <c>GetType().Name</c>.
    /// </summary>
    public string UserTypeName { get; }

    public UserRuntimeError(string typeName, string message, SourceSpan? span = null)
        : base(message, span)
    {
        UserTypeName = typeName;
    }

    public override string ErrorType => UserTypeName;
}
