namespace Stash.Lsp.Analysis;

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Common;

public static class LspExtensions
{
    /// <summary>
    /// Converts a 1-based SourceSpan to a 0-based LSP Range.
    /// </summary>
    public static Range ToLspRange(this SourceSpan span) =>
        new(
            new Position(System.Math.Max(0, span.StartLine - 1), System.Math.Max(0, span.StartColumn - 1)),
            new Position(System.Math.Max(0, span.EndLine - 1), System.Math.Max(0, span.EndColumn - 1))
        );
}
