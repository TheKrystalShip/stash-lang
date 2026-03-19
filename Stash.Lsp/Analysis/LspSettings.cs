namespace Stash.Lsp.Analysis;

using Microsoft.Extensions.Logging;

public class LspSettings
{
    public int DebounceDelayMs { get; set; } = 25;
    public LogLevel LogLevel { get; set; } = LogLevel.Warning;
    public bool InlayHintsEnabled { get; set; } = true;
    public bool CodeLensEnabled { get; set; } = true;
}
