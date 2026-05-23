namespace Stash.Lsp.Completion;

/// <summary>
/// Classifies the cursor context for a completion request, allowing
/// <see cref="CompletionDispatcher"/> to route to the correct provider pipeline.
/// </summary>
public enum CompletionMode
{
    /// <summary>
    /// Cursor is inside an import path string following a <c>from</c> or <c>import</c> keyword.
    /// </summary>
    ImportString,

    /// <summary>
    /// Cursor is immediately after a <c>.</c> that follows an identifier.
    /// </summary>
    Dot,

    /// <summary>
    /// Cursor is in the type-name position after the <c>extend</c> keyword.
    /// </summary>
    AfterExtend,

    /// <summary>
    /// Cursor is in the type-name position after the <c>is</c> keyword.
    /// </summary>
    AfterIs,

    /// <summary>
    /// Anything else — keywords, stdlib globals, and in-scope user-defined symbols.
    /// </summary>
    Default
}
