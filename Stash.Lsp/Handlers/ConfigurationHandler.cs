namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Stash.Lsp.Analysis;

/// <summary>
/// Handles LSP <c>workspace/didChangeConfiguration</c> notifications to propagate
/// editor settings changes to <see cref="LspSettings"/>.
/// </summary>
/// <remarks>
/// <para>
/// The handler reads the incoming JSON settings object under the <c>stash</c> namespace and
/// updates the following <see cref="LspSettings"/> properties:
/// </para>
/// <list type="bullet">
///   <item>
///     <term><c>stash.lsp.debounceTime</c></term>
///     <description>Maps to <see cref="LspSettings.DebounceDelayMs"/> (0–1000 ms).</description>
///   </item>
///   <item>
///     <term><c>stash.lsp.logLevel</c></term>
///     <description>Maps to <see cref="LspSettings.LogLevel"/> (trace/debug/information/warning/error).</description>
///   </item>
///   <item>
///     <term><c>stash.inlayHints.enabled</c></term>
///     <description>Maps to <see cref="LspSettings.InlayHintsEnabled"/>.</description>
///   </item>
///   <item>
///     <term><c>stash.codeLens.enabled</c></term>
///     <description>Maps to <see cref="LspSettings.CodeLensEnabled"/>.</description>
///   </item>
/// </list>
/// <para>
/// Updated settings are immediately visible to all handlers that hold a reference to the
/// shared <see cref="LspSettings"/> singleton.
/// </para>
/// </remarks>
public class ConfigurationHandler : DidChangeConfigurationHandlerBase
{
    private readonly LspSettings _settings;
    private readonly ILogger<ConfigurationHandler> _logger;

    /// <summary>
    /// Initialises the handler with the shared settings object and a logger.
    /// </summary>
    /// <param name="settings">The mutable LSP settings instance shared across all handlers.</param>
    /// <param name="logger">Logger for reporting the applied configuration values.</param>
    public ConfigurationHandler(LspSettings settings, ILogger<ConfigurationHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Processes the configuration change notification and updates <see cref="LspSettings"/> accordingly.
    /// </summary>
    /// <param name="request">
    /// The notification containing the new settings as a JSON object rooted at the workspace configuration.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see cref="Unit.Task"/> after the settings have been applied.</returns>
    public override Task<Unit> Handle(DidChangeConfigurationParams request, CancellationToken cancellationToken)
    {
        if (request.Settings is JObject root)
        {
            var stash = root["stash"] as JObject ?? root;

            // stash.lsp.*
            var lsp = stash["lsp"] as JObject;
            var debounce = lsp?["debounceTime"];
            if (debounce != null && debounce.Type == JTokenType.Integer)
            {
                var value = debounce.Value<int>();
                if (value >= 0 && value <= 1000)
                {
                    _settings.DebounceDelayMs = value;
                }
            }

            var logLevel = lsp?["logLevel"];
            if (logLevel != null && logLevel.Type == JTokenType.String)
            {
                _settings.LogLevel = logLevel.Value<string>() switch
                {
                    "trace" => LogLevel.Trace,
                    "debug" => LogLevel.Debug,
                    "information" => LogLevel.Information,
                    "warning" => LogLevel.Warning,
                    "error" => LogLevel.Error,
                    _ => LogLevel.Warning
                };
            }

            // stash.inlayHints.*
            var inlayHints = stash["inlayHints"] as JObject;
            var inlayEnabled = inlayHints?["enabled"];
            if (inlayEnabled != null && inlayEnabled.Type == JTokenType.Boolean)
            {
                _settings.InlayHintsEnabled = inlayEnabled.Value<bool>();
            }

            // stash.codeLens.*
            var codeLens = stash["codeLens"] as JObject;
            var codeLensEnabled = codeLens?["enabled"];
            if (codeLensEnabled != null && codeLensEnabled.Type == JTokenType.Boolean)
            {
                _settings.CodeLensEnabled = codeLensEnabled.Value<bool>();
            }
        }

        _logger.LogInformation("Configuration updated — logLevel: {LogLevel}, debounce: {Debounce}ms, inlayHints: {Inlay}, codeLens: {CodeLens}",
            _settings.LogLevel, _settings.DebounceDelayMs, _settings.InlayHintsEnabled, _settings.CodeLensEnabled);
        return Unit.Task;
    }
}
