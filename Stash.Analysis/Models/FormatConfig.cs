namespace Stash.Analysis;

using System;
using System.IO;

public enum TrailingCommaStyle { None, All }
public enum EndOfLineStyle { Lf, Crlf, Auto }

/// <summary>
/// Formatter configuration loaded from a <c>.stashformat</c> file.
/// </summary>
/// <remarks>
/// <para>
/// The file uses a simple <c>key=value</c> format (one entry per line). Lines beginning with
/// <c>#</c> are treated as comments and ignored.
/// </para>
/// <example>
/// <code>
/// # .stashformat
/// indentSize=2
/// useTabs=false
/// trailingComma=none
/// endOfLine=lf
/// bracketSpacing=true
/// </code>
/// </example>
/// </remarks>
public sealed class FormatConfig
{
    public int IndentSize { get; init; } = 2;
    public bool UseTabs { get; init; } = false;
    public TrailingCommaStyle TrailingComma { get; init; } = TrailingCommaStyle.None;
    public EndOfLineStyle EndOfLine { get; init; } = EndOfLineStyle.Lf;
    public bool BracketSpacing { get; init; } = true;
    public int PrintWidth { get; init; } = 80;
    public bool SortImports { get; init; } = false;
    public int BlankLinesBetweenBlocks { get; init; } = 1;
    public bool SingleLineBlocks { get; init; } = false;

    /// <summary>A default <see cref="FormatConfig"/> with all settings at their defaults.</summary>
    public static FormatConfig Default { get; } = new();

    /// <summary>
    /// Walks up the directory tree from the directory of <paramref name="filePath"/> searching
    /// for a <c>.stashformat</c> file first, then falls back to <c>.editorconfig</c>.
    /// Returns <see cref="Default"/> if neither is found.
    /// </summary>
    public static FormatConfig LoadWithEditorConfig(string filePath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        var stashFormat = Load(directory);
        if (!ReferenceEquals(stashFormat, Default))
            return stashFormat;

        return EditorConfigParser.LoadForFile(filePath) ?? Default;
    }

    /// <summary>
    /// Walks up the directory tree from <paramref name="directory"/> searching for the first
    /// <c>.stashformat</c> file found. Returns <see cref="Default"/> if none is found.
    /// </summary>
    public static FormatConfig Load(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return Default;

        string? dir = directory;
        while (dir != null)
        {
            string configPath = Path.Combine(dir, ".stashformat");
            if (File.Exists(configPath))
                return LoadFromFile(configPath);

            var parent = Directory.GetParent(dir);
            if (parent == null || parent.FullName == dir) break;
            dir = parent.FullName;
        }

        return Default;
    }

    /// <summary>Loads and parses a <c>.stashformat</c> file at the given absolute path.</summary>
    public static FormatConfig LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return Default;
        return ParseContent(File.ReadAllText(filePath));
    }

    private static FormatConfig ParseContent(string content)
    {
        int indentSize = 2;
        bool useTabs = false;
        var trailingComma = TrailingCommaStyle.None;
        var endOfLine = EndOfLineStyle.Lf;
        bool bracketSpacing = true;
        int printWidth = 80;
        bool sortImports = false;
        int blankLinesBetweenBlocks = 1;
        bool singleLineBlocks = false;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            int eqIndex = line.IndexOf('=');
            if (eqIndex < 0) continue;

            string key = line[..eqIndex].Trim();
            string value = line[(eqIndex + 1)..].Trim();

            switch (key)
            {
                case "indentSize":
                    if (int.TryParse(value, out int parsed) && parsed > 0)
                        indentSize = parsed;
                    break;

                case "useTabs":
                    useTabs = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;

                case "trailingComma":
                    trailingComma = value.ToLowerInvariant() switch
                    {
                        "all" => TrailingCommaStyle.All,
                        _ => TrailingCommaStyle.None
                    };
                    break;

                case "endOfLine":
                    endOfLine = value.ToLowerInvariant() switch
                    {
                        "crlf" => EndOfLineStyle.Crlf,
                        "auto" => EndOfLineStyle.Auto,
                        _ => EndOfLineStyle.Lf
                    };
                    break;

                case "bracketSpacing":
                    bracketSpacing = !value.Equals("false", StringComparison.OrdinalIgnoreCase);
                    break;

                case "printWidth":
                    if (int.TryParse(value, out int parsedWidth) && parsedWidth > 0)
                        printWidth = parsedWidth;
                    break;

                case "sortImports":
                    sortImports = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;

                case "blankLinesBetweenBlocks":
                    if (int.TryParse(value, out int parsedBlank) && parsedBlank >= 1 && parsedBlank <= 2)
                        blankLinesBetweenBlocks = parsedBlank;
                    break;

                case "singleLineBlocks":
                    singleLineBlocks = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        return new FormatConfig
        {
            IndentSize = indentSize,
            UseTabs = useTabs,
            TrailingComma = trailingComma,
            EndOfLine = endOfLine,
            BracketSpacing = bracketSpacing,
            PrintWidth = printWidth,
            SortImports = sortImports,
            BlankLinesBetweenBlocks = blankLinesBetweenBlocks,
            SingleLineBlocks = singleLineBlocks,
        };
    }
}
