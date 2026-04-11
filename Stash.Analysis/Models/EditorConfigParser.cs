namespace Stash.Analysis;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Parses <c>.editorconfig</c> files and extracts Stash-relevant formatting properties.
/// </summary>
internal static class EditorConfigParser
{
    /// <summary>
    /// Given a file path, walks up the directory tree looking for <c>.editorconfig</c> files,
    /// collects properties from sections matching the file, stops at <c>root = true</c>, and
    /// returns a merged <see cref="FormatConfig"/>. Returns <c>null</c> if no
    /// <c>.editorconfig</c> is found or no matching sections exist.
    /// </summary>
    public static FormatConfig? LoadForFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(filePath));

        // Collect editorconfig files from nearest to furthest (nearest wins per property)
        var editorConfigFiles = new List<string>();

        while (dir != null)
        {
            string candidate = Path.Combine(dir, ".editorconfig");
            if (File.Exists(candidate))
                editorConfigFiles.Add(candidate);

            bool isRoot = CheckIsRoot(candidate);
            if (isRoot)
                break;

            var parent = Directory.GetParent(dir);
            if (parent == null || parent.FullName == dir) break;
            dir = parent.FullName;
        }

        if (editorConfigFiles.Count == 0)
            return null;

        // Merge properties: nearest file wins (process nearest first, only set if not already set)
        int? indentSize = null;
        bool? useTabs = null;
        int? printWidth = null;
        EndOfLineStyle? endOfLine = null;
        TrailingCommaStyle? trailingComma = null;
        bool? bracketSpacing = null;
        bool? sortImports = null;
        int? blankLinesBetweenBlocks = null;
        bool? singleLineBlocks = null;

        foreach (string ecFile in editorConfigFiles)
        {
            string content;
            try
            {
                content = File.ReadAllText(ecFile);
            }
            catch (IOException)
            {
                continue;
            }

            var props = ParseMatchingProperties(content, fileName);
            foreach (var (key, value) in props)
            {
                switch (key)
                {
                    case "indent_size":
                        if (indentSize == null && int.TryParse(value, out int parsedIndent) && parsedIndent > 0)
                            indentSize = parsedIndent;
                        break;

                    case "indent_style":
                        if (useTabs == null)
                        {
                            if (value.Equals("tab", StringComparison.OrdinalIgnoreCase))
                                useTabs = true;
                            else if (value.Equals("space", StringComparison.OrdinalIgnoreCase))
                                useTabs = false;
                        }
                        break;

                    case "max_line_length":
                        if (printWidth == null)
                        {
                            if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
                                printWidth = int.MaxValue;
                            else if (int.TryParse(value, out int parsedWidth) && parsedWidth > 0)
                                printWidth = parsedWidth;
                        }
                        break;

                    case "end_of_line":
                        if (endOfLine == null)
                        {
                            endOfLine = value.ToLowerInvariant() switch
                            {
                                "lf" => EndOfLineStyle.Lf,
                                "crlf" => EndOfLineStyle.Crlf,
                                _ => null
                            };
                        }
                        break;

                    case "stash_trailing_comma":
                        if (trailingComma == null)
                        {
                            trailingComma = value.ToLowerInvariant() switch
                            {
                                "all" => TrailingCommaStyle.All,
                                "none" => TrailingCommaStyle.None,
                                _ => null
                            };
                        }
                        break;

                    case "stash_bracket_spacing":
                        if (bracketSpacing == null)
                        {
                            if (bool.TryParse(value, out bool parsedBs))
                                bracketSpacing = parsedBs;
                        }
                        break;

                    case "stash_sort_imports":
                        if (sortImports == null)
                        {
                            if (bool.TryParse(value, out bool parsedSi))
                                sortImports = parsedSi;
                        }
                        break;

                    case "stash_blank_lines_between_blocks":
                        if (blankLinesBetweenBlocks == null)
                        {
                            if (int.TryParse(value, out int parsedBlb) && parsedBlb >= 1 && parsedBlb <= 2)
                                blankLinesBetweenBlocks = parsedBlb;
                        }
                        break;

                    case "stash_single_line_blocks":
                        if (singleLineBlocks == null)
                        {
                            if (bool.TryParse(value, out bool parsedSlb))
                                singleLineBlocks = parsedSlb;
                        }
                        break;
                }
            }
        }

        // If nothing was found, return null
        bool anySet = indentSize != null || useTabs != null || printWidth != null
            || endOfLine != null || trailingComma != null || bracketSpacing != null
            || sortImports != null || blankLinesBetweenBlocks != null || singleLineBlocks != null;

        if (!anySet)
            return null;

        var defaults = FormatConfig.Default;
        return new FormatConfig
        {
            IndentSize = indentSize ?? defaults.IndentSize,
            UseTabs = useTabs ?? defaults.UseTabs,
            PrintWidth = printWidth ?? defaults.PrintWidth,
            EndOfLine = endOfLine ?? defaults.EndOfLine,
            TrailingComma = trailingComma ?? defaults.TrailingComma,
            BracketSpacing = bracketSpacing ?? defaults.BracketSpacing,
            SortImports = sortImports ?? defaults.SortImports,
            BlankLinesBetweenBlocks = blankLinesBetweenBlocks ?? defaults.BlankLinesBetweenBlocks,
            SingleLineBlocks = singleLineBlocks ?? defaults.SingleLineBlocks,
        };
    }

    /// <summary>
    /// Returns <c>true</c> if the <c>.editorconfig</c> file at <paramref name="editorConfigPath"/>
    /// contains <c>root = true</c> outside any section header.
    /// </summary>
    private static bool CheckIsRoot(string editorConfigPath)
    {
        if (!File.Exists(editorConfigPath))
            return false;

        string content;
        try
        {
            content = File.ReadAllText(editorConfigPath);
        }
        catch (IOException)
        {
            return false;
        }

        bool inSection = false;
        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('['))
            {
                inSection = true;
                continue;
            }

            if (!inSection)
            {
                int eq = line.IndexOf('=');
                if (eq >= 0)
                {
                    string key = line[..eq].Trim();
                    string value = line[(eq + 1)..].Trim();
                    if (key.Equals("root", StringComparison.OrdinalIgnoreCase)
                        && value.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Parses an <c>.editorconfig</c> file content and returns all key/value pairs from
    /// sections whose glob pattern matches <paramref name="fileName"/>.
    /// </summary>
    private static List<(string Key, string Value)> ParseMatchingProperties(string content, string fileName)
    {
        var result = new List<(string, string)>();
        bool currentSectionMatches = false;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('['))
            {
                int close = line.IndexOf(']');
                if (close < 0) continue;

                string pattern = line[1..close].Trim();
                currentSectionMatches = MatchesPattern(pattern, fileName);
                continue;
            }

            if (!currentSectionMatches)
                continue;

            int eq = line.IndexOf('=');
            if (eq < 0) continue;

            string key = line[..eq].Trim().ToLowerInvariant();
            string value = line[(eq + 1)..].Trim();
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                result.Add((key, value));
        }

        return result;
    }

    /// <summary>
    /// Matches an editorconfig glob pattern against a file name.
    /// Supported patterns: <c>*</c> (all files), <c>*.ext</c> (single extension),
    /// <c>*.{ext1,ext2}</c> (multiple extensions).
    /// </summary>
    private static bool MatchesPattern(string pattern, string fileName)
    {
        if (pattern == "*")
            return true;

        // *.{ext1,ext2,...}
        if (pattern.StartsWith("*.{") && pattern.EndsWith('}'))
        {
            string inner = pattern[3..^1];
            string[] extensions = inner.Split(',');
            string fileExt = Path.GetExtension(fileName);
            foreach (string ext in extensions)
            {
                string trimmed = ext.Trim();
                if (fileExt.Equals("." + trimmed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // *.ext
        if (pattern.StartsWith("*."))
        {
            string ext = pattern[1..]; // includes the dot
            return Path.GetExtension(fileName).Equals(ext, StringComparison.OrdinalIgnoreCase);
        }

        // Exact filename match
        return string.Equals(Path.GetFileName(fileName), pattern, StringComparison.OrdinalIgnoreCase);
    }
}
