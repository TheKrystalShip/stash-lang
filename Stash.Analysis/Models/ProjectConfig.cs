namespace Stash.Analysis;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Reads and caches project-level diagnostic configuration from <c>.stashcheck</c> files.
/// </summary>
public class ProjectConfig
{
    /// <summary>Diagnostic codes that are globally suppressed.</summary>
    public IReadOnlySet<string> DisabledCodes => _disabledCodes;
    private readonly HashSet<string> _disabledCodes = new();

    /// <summary>Per-code severity overrides.</summary>
    public IReadOnlyDictionary<string, DiagnosticLevel?> SeverityOverrides => _severityOverrides;
    private readonly Dictionary<string, DiagnosticLevel?> _severityOverrides = new();

    /// <summary>Whether suppression directives must include a reason.</summary>
    public bool RequireSuppressionReason { get; private set; }

    /// <summary>An empty config with no overrides.</summary>
    public static readonly ProjectConfig Empty = new();

    /// <summary>
    /// Searches for a <c>.stashcheck</c> file starting from <paramref name="scriptDirectory"/>
    /// and walking up to the filesystem root. Returns <see cref="Empty"/> if no file is found.
    /// </summary>
    public static ProjectConfig Load(string? scriptDirectory)
    {
        if (string.IsNullOrEmpty(scriptDirectory)) return Empty;

        var dir = scriptDirectory;
        while (dir != null)
        {
            var configPath = Path.Combine(dir, ".stashcheck");
            if (File.Exists(configPath))
            {
                return ParseFile(configPath);
            }
            var parent = Directory.GetParent(dir);
            if (parent == null || parent.FullName == dir) break;
            dir = parent.FullName;
        }

        return Empty;
    }

    /// <summary>
    /// Parses a <c>.stashcheck</c> file from the given content string.
    /// </summary>
    public static ProjectConfig ParseContent(string content)
    {
        var config = new ProjectConfig();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            var eqIndex = line.IndexOf('=');
            if (eqIndex < 0) continue;

            var key = line[..eqIndex].Trim();
            var value = line[(eqIndex + 1)..].Trim();

            if (key == "disable")
            {
                foreach (var code in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    config._disabledCodes.Add(code);
                }
            }
            else if (key.StartsWith("severity."))
            {
                var code = key["severity.".Length..];
                if (TryParseSeverity(value, out var level))
                {
                    config._severityOverrides[code] = level;
                }
            }
            else if (key == "require-suppression-reason")
            {
                config.RequireSuppressionReason = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }
        return config;
    }

    private static ProjectConfig ParseFile(string path)
    {
        var content = File.ReadAllText(path);
        return ParseContent(content);
    }

    private static bool TryParseSeverity(string value, out DiagnosticLevel? level)
    {
        switch (value.ToLowerInvariant())
        {
            case "error": level = DiagnosticLevel.Error; return true;
            case "warning": level = DiagnosticLevel.Warning; return true;
            case "info":
            case "information": level = DiagnosticLevel.Information; return true;
            case "off": level = null; return true;
            default: level = null; return false;
        }
    }

    /// <summary>
    /// Applies project-level configuration to a list of diagnostics:
    /// removes globally disabled codes and applies severity overrides.
    /// </summary>
    public List<SemanticDiagnostic> Apply(List<SemanticDiagnostic> diagnostics)
    {
        var result = new List<SemanticDiagnostic>(diagnostics.Count);
        foreach (var d in diagnostics)
        {
            // Check if globally disabled
            if (d.Code != null && _disabledCodes.Contains(d.Code)) continue;

            // Check severity override
            if (d.Code != null && _severityOverrides.TryGetValue(d.Code, out var overrideLevel))
            {
                if (overrideLevel == null) continue; // "off" means suppress entirely
                // Create a new diagnostic with the overridden severity
                result.Add(new SemanticDiagnostic(d.Code, d.Message, overrideLevel.Value, d.Span, d.IsUnnecessary));
                continue;
            }

            result.Add(d);
        }
        return result;
    }
}
