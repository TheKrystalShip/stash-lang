namespace Stash.Lsp.Analysis;

using Microsoft.Extensions.Logging;

/// <summary>
/// Runtime configuration settings for the Stash language server.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton by <see cref="StashLanguageServer"/> and injected where
/// server-wide configuration is required (e.g., <c>TextDocumentSyncHandler</c> for the
/// debounce delay, and the OmniSharp logging pipeline for the minimum log level).
/// </para>
/// <para>
/// Settings can be updated at runtime via the <c>ConfigurationHandler</c>, which handles
/// LSP <c>workspace/didChangeConfiguration</c> notifications.
/// </para>
/// </remarks>
public class LspSettings
{
    /// <summary>
    /// Gets or sets the debounce delay in milliseconds applied after a document change
    /// before triggering a re-analysis. Defaults to <c>25</c> ms.
    /// </summary>
    public int DebounceDelayMs { get; set; } = 25;

    /// <summary>
    /// Gets or sets the minimum <see cref="LogLevel"/> forwarded to the OmniSharp logging pipeline.
    /// Defaults to <see cref="LogLevel.Warning"/>.
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Warning;

    /// <summary>
    /// Gets or sets whether inlay hints (type hints shown inline by the editor) are enabled.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool InlayHintsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether code-lens annotations (reference counts, run buttons, etc.) are enabled.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool CodeLensEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether background workspace indexing is enabled.
    /// When enabled, the server scans all <c>.stash</c> files in the workspace to build
    /// a complete reference index. Defaults to <see langword="false"/>.
    /// </summary>
    public bool WorkspaceIndexingEnabled { get; set; } = false;
}
