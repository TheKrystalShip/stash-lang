using System;
using Stash.Common;

namespace Stash.Bytecode;

/// <summary>
/// Thrown by the <see cref="Compiler"/> when it encounters a construct it cannot compile —
/// for example, a <c>break</c> or <c>continue</c> that appears outside of any loop.
/// </summary>
public sealed class CompileError : Exception
{
    /// <summary>The source location where the compile error occurred.</summary>
    public SourceSpan Span { get; }

    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="span">The source span where the error was detected.</param>
    public CompileError(string message, SourceSpan span) : base(message)
    {
        Span = span;
    }
}
