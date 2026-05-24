namespace Stash.Lsp.Completion.Providers;

using System;
using System.Collections.Generic;
using System.IO;
using Stash.Common;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Provides package-name completion candidates when the cursor is inside an import-path
/// string (i.e., the string argument to a <c>from … import</c> or bare <c>import</c>
/// statement). Non-import strings yield no candidates, which causes the dispatcher to
/// return an empty <see cref="OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionList"/>.
/// </summary>
/// <remarks>
/// <para>
/// Applies exclusively to <see cref="CompletionMode.ImportString"/> mode. Scans the
/// <c>stashes/</c> directory under the project root (located via
/// <see cref="ModuleResolver.FindProjectRoot"/>) and emits one candidate per package,
/// including <c>@scope/name</c> scoped entries.
/// </para>
/// <para>
/// Candidate <see cref="CompletionCandidate.Accessibility"/> is not set (unclassified)
/// because import-path strings are not subject to the bare-identifier accessibility
/// invariant.
/// </para>
/// </remarks>
public sealed class ImportPathCompletionProvider : ICompletionProvider
{
    /// <inheritdoc />
    public bool AppliesTo(CompletionContext ctx) => ctx.Mode == CompletionMode.ImportString;

    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        if (ctx.CurrentLine == null)
        {
            yield break;
        }

        // Check whether the cursor is in an import/from context (not just any string)
        if (!IsImportContext(ctx.CurrentLine, ctx.LspColumn))
        {
            yield break;
        }

        // Find project root to list packages from stashes/
        string? documentDir = null;
        if (ctx.Uri.IsFile)
        {
            documentDir = Path.GetDirectoryName(ctx.Uri.LocalPath);
        }

        if (documentDir == null)
        {
            yield break;
        }

        string? projectRoot = ModuleResolver.FindProjectRoot(documentDir);
        if (projectRoot == null)
        {
            yield break;
        }

        string stashesDir = Path.Combine(projectRoot, "stashes");
        if (!Directory.Exists(stashesDir))
        {
            yield break;
        }

        // List direct package directories
        foreach (string dir in Directory.GetDirectories(stashesDir))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith('@'))
            {
                // Scoped packages: list @scope/name entries
                foreach (string scopedDir in Directory.GetDirectories(dir))
                {
                    string scopedName = name + "/" + Path.GetFileName(scopedDir);
                    yield return new CompletionCandidate(
                        Label: scopedName,
                        Kind: LspCompletionItemKind.Module,
                        Detail: "package",
                        SourcePriority: 10,
                        SourceTag: nameof(ImportPathCompletionProvider));
                }
            }
            else
            {
                yield return new CompletionCandidate(
                    Label: name,
                    Kind: LspCompletionItemKind.Module,
                    Detail: "package",
                    SourcePriority: 10,
                    SourceTag: nameof(ImportPathCompletionProvider));
            }
        }
    }

    /// <summary>
    /// Determines whether the cursor position within <paramref name="line"/> is inside
    /// a string that is part of an <c>import</c> or <c>from … import</c> statement.
    /// </summary>
    /// <param name="line">The source line text.</param>
    /// <param name="col">The 0-based cursor column.</param>
    /// <returns>
    /// <see langword="true"/> if the string is an import context;
    /// <see langword="false"/> otherwise.
    /// </returns>
    internal static bool IsImportContext(string line, int col)
    {
        // Find the opening quote before the cursor
        int quoteStart = -1;
        for (int i = col - 1; i >= 0; i--)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                quoteStart = i;
                break;
            }
        }

        if (quoteStart < 0)
        {
            return false;
        }

        // Check if the text before the quote indicates an import context
        string before = line[..quoteStart].TrimEnd();
        bool endsWithFrom = before.EndsWith("from", StringComparison.Ordinal) &&
                            (before.Length == 4 || !char.IsLetterOrDigit(before[before.Length - 5]));
        bool isImportContext = endsWithFrom ||
                               (before.StartsWith("import", StringComparison.Ordinal) && !before.Contains('{'));

        return isImportContext;
    }
}
