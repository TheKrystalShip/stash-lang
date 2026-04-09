namespace Stash.Check;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stash.Analysis;
using Stash.Common;

internal sealed class SarifFormatter : IOutputFormatter
{
    public string Format => "sarif";

    private readonly string _commandLine;
    private readonly DateTime _startTime;

    public SarifFormatter(string commandLine, DateTime startTime)
    {
        _commandLine = commandLine;
        _startTime = startTime;
    }

    public void Write(CheckResult result, Stream output)
    {
        var sarifLog = BuildSarifLog(result);
        JsonSerializer.Serialize(output, sarifLog, SarifJsonContext.Default.SarifLog);
    }

    private SarifLog BuildSarifLog(CheckResult result)
    {
        string cwd = Directory.GetCurrentDirectory();
        string cwdUri = new Uri(cwd + Path.DirectorySeparatorChar).AbsoluteUri;

        var rules = BuildRules();
        var ruleIndex = new Dictionary<string, int>();
        for (int i = 0; i < rules.Count; i++)
        {
            ruleIndex[rules[i].Id!] = i;
        }

        var results = new List<SarifResult>();

        foreach (var file in result.Files)
        {
            // Lex errors → STASH001
            foreach (var err in file.Analysis.StructuredLexErrors)
            {
                results.Add(new SarifResult
                {
                    RuleId = "STASH001",
                    RuleIndex = ruleIndex.TryGetValue("STASH001", out int li) ? li : null,
                    Level = "error",
                    Kind = "fail",
                    Message = new SarifMessage { Text = err.Message },
                    Locations = new List<SarifLocation>
                    {
                        BuildLocation(file.Uri, err.Span, cwd)
                    }
                });
            }

            // Parse errors → STASH002
            foreach (var err in file.Analysis.StructuredParseErrors)
            {
                results.Add(new SarifResult
                {
                    RuleId = "STASH002",
                    RuleIndex = ruleIndex.TryGetValue("STASH002", out int pi) ? pi : null,
                    Level = "error",
                    Kind = "fail",
                    Message = new SarifMessage { Text = err.Message },
                    Locations = new List<SarifLocation>
                    {
                        BuildLocation(file.Uri, err.Span, cwd)
                    }
                });
            }

            // Semantic diagnostics
            foreach (var diag in file.Analysis.SemanticDiagnostics)
            {
                string ruleId = diag.Code ?? "SA0000";
                string level = diag.Level switch
                {
                    DiagnosticLevel.Error => "error",
                    DiagnosticLevel.Warning => "warning",
                    DiagnosticLevel.Information => "note",
                    _ => "none"
                };

                var sarifResult = new SarifResult
                {
                    RuleId = ruleId,
                    RuleIndex = ruleIndex.TryGetValue(ruleId, out int si) ? si : null,
                    Level = level,
                    Message = new SarifMessage { Text = diag.Message },
                    Locations = new List<SarifLocation>
                    {
                        BuildLocation(file.Uri, diag.Span, cwd)
                    }
                };

                if (diag.IsUnnecessary)
                {
                    sarifResult.Properties = new Dictionary<string, object>
                    {
                        ["tags"] = new[] { "unnecessary" }
                    };
                }

                results.Add(sarifResult);
            }
        }

        var run = new SarifRun
        {
            Tool = new SarifTool
            {
                Driver = new SarifToolComponent
                {
                    Name = "stash-check",
                    SemanticVersion = GetVersion(),
                    InformationUri = "https://stash-lang.dev",
                    Rules = rules
                }
            },
            OriginalUriBaseIds = new Dictionary<string, SarifArtifactLocation>
            {
                ["%SRCROOT%"] = new SarifArtifactLocation { Uri = cwdUri }
            },
            Results = results,
            Invocations = new List<SarifInvocation>
            {
                new SarifInvocation
                {
                    ExecutionSuccessful = true,
                    CommandLine = _commandLine,
                    StartTimeUtc = _startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    EndTimeUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                }
            }
        };

        return new SarifLog
        {
            Schema = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json",
            Version = "2.1.0",
            Runs = new List<SarifRun> { run }
        };
    }

    private static SarifLocation BuildLocation(Uri fileUri, SourceSpan span, string cwd)
    {
        string filePath = fileUri.IsFile ? fileUri.LocalPath : fileUri.ToString();
        string relativePath = Path.GetRelativePath(cwd, filePath).Replace('\\', '/');

        // If the relative path starts with ".." it's outside cwd, use absolute file:// URI
        bool isRelative = !relativePath.StartsWith("..", StringComparison.Ordinal);
        string artifactUri = isRelative ? relativePath : fileUri.AbsoluteUri;
        string? uriBaseId = isRelative ? "%SRCROOT%" : null;

        return new SarifLocation
        {
            PhysicalLocation = new SarifPhysicalLocation
            {
                ArtifactLocation = new SarifArtifactLocation
                {
                    Uri = artifactUri,
                    UriBaseId = uriBaseId
                },
                Region = new SarifRegion
                {
                    StartLine = span.StartLine,
                    StartColumn = span.StartColumn,
                    EndLine = span.EndLine,
                    EndColumn = span.EndColumn
                }
            }
        };
    }

    private static List<SarifReportingDescriptor> BuildRules()
    {
        var rules = new List<SarifReportingDescriptor>();

        // Add lex/parse error rules first
        rules.Add(new SarifReportingDescriptor
        {
            Id = "STASH001",
            ShortDescription = new SarifMessage { Text = "Lexer error" },
            DefaultConfiguration = new SarifConfiguration { Level = "error" },
            Properties = new Dictionary<string, object> { ["category"] = "Syntax" }
        });
        rules.Add(new SarifReportingDescriptor
        {
            Id = "STASH002",
            ShortDescription = new SarifMessage { Text = "Parser error" },
            DefaultConfiguration = new SarifConfiguration { Level = "error" },
            Properties = new Dictionary<string, object> { ["category"] = "Syntax" }
        });

        // Add all DiagnosticDescriptors
        foreach (var kvp in DiagnosticDescriptors.AllByCode.OrderBy(k => k.Key))
        {
            string level = kvp.Value.DefaultLevel switch
            {
                DiagnosticLevel.Error => "error",
                DiagnosticLevel.Warning => "warning",
                DiagnosticLevel.Information => "note",
                _ => "none"
            };

            rules.Add(new SarifReportingDescriptor
            {
                Id = kvp.Value.Code,
                ShortDescription = new SarifMessage { Text = kvp.Value.Title },
                HelpUri = kvp.Value.HelpUrl,
                DefaultConfiguration = new SarifConfiguration { Level = level },
                Properties = new Dictionary<string, object> { ["category"] = kvp.Value.Category }
            });
        }

        return rules;
    }

    private static string GetVersion()
    {
        var assembly = typeof(SarifFormatter).Assembly;
        var version = assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.1.0";
    }
}

// ── SARIF Model Classes ──────────────────────────────────────────────

internal sealed class SarifLog
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }
    public string? Version { get; set; }
    public List<SarifRun>? Runs { get; set; }
}

internal sealed class SarifRun
{
    public SarifTool? Tool { get; set; }
    public Dictionary<string, SarifArtifactLocation>? OriginalUriBaseIds { get; set; }
    public List<SarifResult>? Results { get; set; }
    public List<SarifInvocation>? Invocations { get; set; }
}

internal sealed class SarifTool
{
    public SarifToolComponent? Driver { get; set; }
}

internal sealed class SarifToolComponent
{
    public string? Name { get; set; }
    public string? SemanticVersion { get; set; }
    public string? InformationUri { get; set; }
    public List<SarifReportingDescriptor>? Rules { get; set; }
}

internal sealed class SarifReportingDescriptor
{
    public string? Id { get; set; }
    public SarifMessage? ShortDescription { get; set; }
    public string? HelpUri { get; set; }
    public SarifConfiguration? DefaultConfiguration { get; set; }
    public Dictionary<string, object>? Properties { get; set; }
}

internal sealed class SarifConfiguration
{
    public string? Level { get; set; }
}

internal sealed class SarifResult
{
    public string? RuleId { get; set; }
    public int? RuleIndex { get; set; }
    public string? Level { get; set; }
    public string? Kind { get; set; }
    public SarifMessage? Message { get; set; }
    public List<SarifLocation>? Locations { get; set; }
    public Dictionary<string, object>? Properties { get; set; }
}

internal sealed class SarifMessage
{
    public string? Text { get; set; }
}

internal sealed class SarifLocation
{
    public SarifPhysicalLocation? PhysicalLocation { get; set; }
}

internal sealed class SarifPhysicalLocation
{
    public SarifArtifactLocation? ArtifactLocation { get; set; }
    public SarifRegion? Region { get; set; }
}

internal sealed class SarifArtifactLocation
{
    public string? Uri { get; set; }
    public string? UriBaseId { get; set; }
}

internal sealed class SarifRegion
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
}

internal sealed class SarifInvocation
{
    public bool ExecutionSuccessful { get; set; }
    public string? CommandLine { get; set; }
    public string? StartTimeUtc { get; set; }
    public string? EndTimeUtc { get; set; }
}
