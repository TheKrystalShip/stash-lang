namespace Stash.Lsp.Handlers;

using System.Text;
using Stash.Stdlib.Models;

/// <summary>
/// Shared helper that renders a <c>ThrowsEntry[]</c> list into a Markdown "Throws" section,
/// used by <see cref="HoverHandler"/>, <see cref="SignatureHelpHandler"/>, and
/// <see cref="CompletionHandler"/>.
/// </summary>
internal static class ThrowsRenderer
{
    /// <summary>
    /// Renders the throws section as a Markdown string, or returns <see langword="null"/> when
    /// <paramref name="throws"/> is <see langword="null"/> or empty.
    /// </summary>
    public static string? Render(ThrowsEntry[]? throws)
    {
        if (throws is not { Length: > 0 }) return null;

        var sb = new StringBuilder();
        sb.Append("\n\n**Throws:**");
        foreach (var entry in throws)
        {
            sb.Append("\n- `").Append(entry.ErrorType).Append('`');
            if (!string.IsNullOrEmpty(entry.Description))
                sb.Append(" — ").Append(entry.Description);
        }

        return sb.ToString();
    }
}
