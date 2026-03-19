namespace Stash.Lsp.Handlers;

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using Stash.Lsp.Analysis;

public class ConfigurationHandler : DidChangeConfigurationHandlerBase
{
    private readonly LspSettings _settings;
    private readonly ILogger<ConfigurationHandler> _logger;

    public ConfigurationHandler(LspSettings settings, ILogger<ConfigurationHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

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
