namespace Stash.Lsp.Completion;

using System;
using Stash.Analysis;

/// <summary>
/// Carries all inputs that completion providers need for a single request.
/// Cheap to construct; built exactly once per request by the dispatcher.
/// </summary>
/// <param name="Uri">The document URI of the file being completed.</param>
/// <param name="LspLine">The 0-based LSP line index of the cursor.</param>
/// <param name="LspColumn">The 0-based LSP column index of the cursor.</param>
/// <param name="CurrentLine">
/// The full text of the line at <paramref name="LspLine"/>,
/// or <see langword="null"/> when the document text is unavailable.
/// </param>
/// <param name="Mode">
/// The dispatcher's classification of the cursor context for this request.
/// </param>
/// <param name="DotPrefix">
/// The identifier that appears before the <c>.</c> in <see cref="CompletionMode.Dot"/> mode,
/// or <see langword="null"/> in all other modes.
/// </param>
/// <param name="Analysis">
/// The cached <see cref="AnalysisResult"/> for the document,
/// or <see langword="null"/> when no cached result is available.
/// </param>
/// <param name="TriggerCharacter">
/// The character that triggered completion (from <c>CompletionParams.Context</c>),
/// or <see langword="null"/> when completion was triggered programmatically.
/// </param>
public sealed record CompletionContext(
    Uri Uri,
    int LspLine,
    int LspColumn,
    string? CurrentLine,
    CompletionMode Mode,
    string? DotPrefix,
    AnalysisResult? Analysis,
    char? TriggerCharacter);
