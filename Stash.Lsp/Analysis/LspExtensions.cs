namespace Stash.Lsp.Analysis;

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Common;

/// <summary>
/// Extension methods that bridge Stash.Core span types to OmniSharp LSP protocol models.
/// </summary>
/// <remarks>
/// Provides conversions between the 1-based, inclusive coordinate system used by
/// <see cref="SourceSpan"/> and the 0-based, end-exclusive <see cref="Range"/> model
/// required by the Language Server Protocol.
/// </remarks>
public static class LspExtensions
{
    /// <summary>
    /// Converts a 1-based inclusive <see cref="SourceSpan"/> to a 0-based LSP <see cref="Range"/> (end-exclusive).
    /// </summary>
    /// <remarks>
    /// The start position is shifted by <c>-1</c> on both line and column axes.
    /// The end column is not decremented because LSP ranges are exclusive of the end character,
    /// which aligns with the inclusive end column stored in <see cref="SourceSpan"/>.
    /// Negative coordinates are clamped to zero.
    /// </remarks>
    /// <param name="span">The source span using 1-based line and column numbers.</param>
    /// <returns>An LSP <see cref="Range"/> with 0-based positions suitable for OmniSharp protocol responses.</returns>
    public static Range ToLspRange(this SourceSpan span) =>
        new(
            new Position(System.Math.Max(0, span.StartLine - 1), System.Math.Max(0, span.StartColumn - 1)),
            new Position(System.Math.Max(0, span.EndLine - 1), System.Math.Max(0, span.EndColumn))
        );
}
