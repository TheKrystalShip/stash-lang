namespace Stash.Analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Reads and caches project-level diagnostic configuration from <c>.stashcheck</c> files.
/// Supports hierarchical loading, per-file overrides, prefix-based disabling, presets,
/// and CLI-level rule selection.
/// </summary>
public class ProjectConfig
{
    /// <summary>Diagnostic codes (or prefixes) that are globally suppressed.</summary>
    public IReadOnlySet<string> DisabledCodes => _disabledCodes;
    private readonly HashSet<string> _disabledCodes = new();

    /// <summary>Diagnostic codes (or prefixes) that are explicitly re-enabled, overriding disabled entries.</summary>
    public IReadOnlySet<string> EnabledCodes => _enabledCodes;
    private readonly HashSet<string> _enabledCodes = new();

    /// <summary>Per-code severity overrides.</summary>
    public IReadOnlyDictionary<string, DiagnosticLevel?> SeverityOverrides => _severityOverrides;
    private readonly Dictionary<string, DiagnosticLevel?> _severityOverrides = new();

    /// <summary>Whether suppression directives must include a reason.</summary>
    public bool RequireSuppressionReason { get; private set; }

    /// <summary>Active rule preset: <c>recommended</c>, <c>strict</c>, or <c>minimal</c>.</summary>
    public string? Preset { get; private set; }

    /// <summary>Per-file glob → override map for the <c>[per-file-overrides]</c> section.</summary>
    public IReadOnlyDictionary<string, PerFileOverride> PerFileOverrides => _perFileOverrides;
    private readonly Dictionary<string, PerFileOverride> _perFileOverrides = new();

    /// <summary>
    /// CLI-supplied exclusive allow-list. When non-empty, only codes/prefixes in this set are
    /// reported; all others are suppressed.
    /// </summary>
    private readonly HashSet<string> _selectCodes = new();

    /// <summary>Extend paths collected during config parsing (internal, resolved by ParseFile).</summary>
    private readonly List<string> _extendPaths = new();

    /// <summary>An empty config with no overrides.</summary>
    public static readonly ProjectConfig Empty = new();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> if the given diagnostic code should be suppressed, considering
    /// the exclusive select list, enabled overrides, preset, exact disabled codes, and prefix matches.
    /// </summary>
    public bool IsCodeDisabled(string code)
    {
        // If an exclusive select list is active, the code must appear in it (exact or prefix)
        if (_selectCodes.Count > 0)
        {
            bool inSelect = _selectCodes.Contains(code);
            if (!inSelect)
            {
                foreach (string entry in _selectCodes)
                {
                    if (entry.Length < 6 && code.StartsWith(entry, StringComparison.Ordinal))
                    {
                        inSelect = true;
                        break;
                    }
                }
            }
            if (!inSelect) return true;
        }

        // Explicitly enabled codes override any disabled entry
        if (_enabledCodes.Contains(code)) return false;
        foreach (string entry in _enabledCodes)
        {
            if (entry.Length < 6 && code.StartsWith(entry, StringComparison.Ordinal))
                return false;
        }

        // Preset: "minimal" disables all non-Error rules
        if (Preset == "minimal" &&
            DiagnosticDescriptors.AllByCode.TryGetValue(code, out var desc) &&
            desc.DefaultLevel != DiagnosticLevel.Error)
        {
            return true;
        }

        // Exact disabled-code match
        if (_disabledCodes.Contains(code)) return true;

        // Prefix-based disabled match (entries shorter than a full 6-char code)
        foreach (string entry in _disabledCodes)
        {
            if (entry.Length < 6 && code.StartsWith(entry, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns a new <see cref="ProjectConfig"/> with CLI <c>--select</c> and <c>--ignore</c>
    /// overrides merged in. Returns <see langword="this"/> when both lists are empty.
    /// </summary>
    public ProjectConfig WithCliOverrides(IReadOnlyList<string>? select, IReadOnlyList<string>? ignore)
    {
        if ((select == null || select.Count == 0) && (ignore == null || ignore.Count == 0))
            return this;

        var result = Clone();

        if (select != null)
        {
            foreach (string code in select)
                result._selectCodes.Add(code.Trim());
        }

        if (ignore != null)
        {
            foreach (string code in ignore)
                result._disabledCodes.Add(code.Trim());
        }

        return result;
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up the directory tree from <paramref name="scriptDirectory"/> to the filesystem root,
    /// collecting ALL <c>.stashcheck</c> files found. Merges them root-first so that closer
    /// configs override ancestor settings. Returns <see cref="Empty"/> if none are found.
    /// </summary>
    public static ProjectConfig Load(string? scriptDirectory)
    {
        if (string.IsNullOrEmpty(scriptDirectory)) return Empty;

        var configPaths = new List<string>();
        string? dir = scriptDirectory;
        while (dir != null)
        {
            string configPath = Path.Combine(dir, ".stashcheck");
            if (File.Exists(configPath))
                configPaths.Add(configPath);
            var parent = Directory.GetParent(dir);
            if (parent == null || parent.FullName == dir) break;
            dir = parent.FullName;
        }

        if (configPaths.Count == 0) return Empty;

        // Reverse so root config is first; child configs override parent
        configPaths.Reverse();

        var merged = ParseFile(configPaths[0]);
        for (int i = 1; i < configPaths.Count; i++)
            merged = Merge(merged, ParseFile(configPaths[i]));

        return merged;
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>.stashcheck</c> config from an in-memory string.
    /// Supports global directives and a <c>[per-file-overrides]</c> section.
    /// </summary>
    public static ProjectConfig ParseContent(string content)
    {
        var config = new ProjectConfig();
        bool inPerFileOverrides = false;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            // Section header
            if (line.StartsWith('['))
            {
                inPerFileOverrides = line.Equals("[per-file-overrides]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inPerFileOverrides)
            {
                ParsePerFileOverrideLine(config, line);
                continue;
            }

            int eqIndex = line.IndexOf('=');
            if (eqIndex < 0) continue;

            string key = line[..eqIndex].Trim();
            string value = line[(eqIndex + 1)..].Trim();

            switch (key)
            {
                case "disable":
                    foreach (string code in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        config._disabledCodes.Add(code);
                    break;

                case "enable":
                    foreach (string code in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        config._enabledCodes.Add(code);
                    break;

                case "preset":
                    string presetVal = value.ToLowerInvariant();
                    if (presetVal is "recommended" or "strict" or "minimal")
                        config.Preset = presetVal;
                    break;

                case "extend":
                    config._extendPaths.Add(value);
                    break;

                case "require-suppression-reason":
                    config.RequireSuppressionReason = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;

                default:
                    if (key.StartsWith("severity."))
                    {
                        string code = key["severity.".Length..];
                        if (TryParseSeverity(value, out var level))
                            config._severityOverrides[code] = level;
                    }
                    break;
            }
        }

        return config;
    }

    private static void ParsePerFileOverrideLine(ProjectConfig config, string line)
    {
        // Format: "glob-pattern" = disable SA0201, SA0206
        //     or: "glob-pattern" = disable ALL
        if (!line.StartsWith('"')) return;

        int closeQuote = line.IndexOf('"', 1);
        if (closeQuote < 0) return;

        string glob = line[1..closeQuote];

        int eqIndex = line.IndexOf('=', closeQuote);
        if (eqIndex < 0) return;

        string action = line[(eqIndex + 1)..].Trim();

        if (action.StartsWith("disable", StringComparison.OrdinalIgnoreCase))
        {
            string rest = action["disable".Length..].Trim();
            if (rest.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                config._perFileOverrides[glob] = new PerFileOverride { DisableAll = true };
            }
            else
            {
                var codes = new HashSet<string>(StringComparer.Ordinal);
                foreach (string code in rest.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    codes.Add(code);
                config._perFileOverrides[glob] = new PerFileOverride { DisabledCodes = codes };
            }
        }
    }

    private static ProjectConfig ParseFile(string path)
    {
        string content = File.ReadAllText(path);
        var config = ParseContent(content);
        string dir = Path.GetDirectoryName(path) ?? "";

        // Process extend chains: treat each extended config as a parent
        ProjectConfig? extendBase = null;
        foreach (string extendPath in config._extendPaths)
        {
            string resolved = Path.IsPathRooted(extendPath)
                ? extendPath
                : Path.Combine(dir, extendPath);

            if (File.Exists(resolved))
            {
                var extended = ParseFile(resolved); // recursive, handles circular guard implicitly via OS path limits
                extendBase = extendBase == null ? extended : Merge(extendBase, extended);
            }
        }

        return extendBase != null ? Merge(extendBase, config) : config;
    }

    // ── Merging ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Merges two configs. <paramref name="child"/> settings take precedence over
    /// <paramref name="parent"/> for same keys; disabled/enabled code sets are unioned.
    /// </summary>
    private static ProjectConfig Merge(ProjectConfig parent, ProjectConfig child)
    {
        var merged = new ProjectConfig();

        // Disabled codes: child adds to parent's set
        merged._disabledCodes.UnionWith(parent._disabledCodes);
        merged._disabledCodes.UnionWith(child._disabledCodes);

        // Enabled codes: child adds to parent's set
        merged._enabledCodes.UnionWith(parent._enabledCodes);
        merged._enabledCodes.UnionWith(child._enabledCodes);

        // Severity overrides: child overrides parent for same key
        foreach (var kv in parent._severityOverrides)
            merged._severityOverrides[kv.Key] = kv.Value;
        foreach (var kv in child._severityOverrides)
            merged._severityOverrides[kv.Key] = kv.Value;

        // RequireSuppressionReason: true if either config requires it
        merged.RequireSuppressionReason = parent.RequireSuppressionReason || child.RequireSuppressionReason;

        // Preset: child overrides if set
        merged.Preset = child.Preset ?? parent.Preset;

        // Per-file overrides: child's patterns override parent's for same glob
        foreach (var kv in parent._perFileOverrides)
            merged._perFileOverrides[kv.Key] = kv.Value;
        foreach (var kv in child._perFileOverrides)
            merged._perFileOverrides[kv.Key] = kv.Value;

        return merged;
    }

    // ── Applying ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies project-level configuration to a list of diagnostics: removes disabled codes,
    /// applies per-file overrides for <paramref name="filePath"/>, and applies severity overrides.
    /// </summary>
    /// <param name="diagnostics">The diagnostics to filter.</param>
    /// <param name="filePath">Optional absolute file path for per-file override matching.</param>
    public List<SemanticDiagnostic> Apply(List<SemanticDiagnostic> diagnostics, string? filePath = null)
    {
        var fileOverride = FindPerFileOverride(filePath);

        var result = new List<SemanticDiagnostic>(diagnostics.Count);
        foreach (var d in diagnostics)
        {
            string? code = d.Code;

            // Per-file override check
            if (fileOverride != null && code != null)
            {
                if (fileOverride.DisableAll) continue;
                if (fileOverride.DisabledCodes.Contains(code)) continue;
            }

            // Global disable check (covers exact, prefix, select, preset)
            if (code != null && IsCodeDisabled(code)) continue;

            // Severity override
            if (code != null && _severityOverrides.TryGetValue(code, out var overrideLevel))
            {
                if (overrideLevel == null) continue; // "off"
                result.Add(new SemanticDiagnostic(code, d.Message, overrideLevel.Value, d.Span, d.IsUnnecessary));
                continue;
            }

            result.Add(d);
        }
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private PerFileOverride? FindPerFileOverride(string? filePath)
    {
        if (filePath == null || _perFileOverrides.Count == 0) return null;
        string normalizedPath = filePath.Replace('\\', '/');
        foreach (var (glob, overrideSpec) in _perFileOverrides)
        {
            if (GlobMatches(normalizedPath, glob))
                return overrideSpec;
        }
        return null;
    }

    private static bool GlobMatches(string path, string glob)
    {
        string pattern = "^" + Regex.Escape(glob)
            .Replace("\\*\\*/", "(.+/)?")
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(path, pattern, RegexOptions.IgnoreCase);
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

    private ProjectConfig Clone()
    {
        var result = new ProjectConfig();
        result._disabledCodes.UnionWith(_disabledCodes);
        result._enabledCodes.UnionWith(_enabledCodes);
        result._selectCodes.UnionWith(_selectCodes);
        result.RequireSuppressionReason = RequireSuppressionReason;
        result.Preset = Preset;
        foreach (var kv in _severityOverrides)
            result._severityOverrides[kv.Key] = kv.Value;
        foreach (var kv in _perFileOverrides)
            result._perFileOverrides[kv.Key] = kv.Value;
        return result;
    }
}
