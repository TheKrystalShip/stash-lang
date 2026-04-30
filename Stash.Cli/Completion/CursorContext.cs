using System;
using System.Collections.Generic;

namespace Stash.Cli.Completion;

internal enum CompletionMode
{
    Shell,
    Stash,
    Substitution,
    None
}

internal sealed record CursorContext(
    CompletionMode Mode,
    int ReplaceStart,
    int ReplaceEnd,
    string TokenText,
    bool InQuote,
    char QuoteChar,
    bool InSubstitution,
    IReadOnlyList<string> PriorArgs)
{
    public static readonly CursorContext Empty = new(
        Mode: CompletionMode.None,
        ReplaceStart: 0,
        ReplaceEnd: 0,
        TokenText: string.Empty,
        InQuote: false,
        QuoteChar: '\0',
        InSubstitution: false,
        PriorArgs: Array.Empty<string>());
}
