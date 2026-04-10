namespace Stash.Format;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Stash.Analysis;

internal static class Program
{
    private const string Usage = @"Usage: stash-format [OPTIONS] [FILES/DIRS...]

Formats Stash source files.

Arguments:
  FILES/DIRS...              One or more .stash files or directories (default: .)

Options:
  -w, --write                Format files in place (overwrite)
  -c, --check                Exit 1 if any file needs formatting (CI mode)
  -d, --diff                 Print unified diff of changes
  -i, --indent-size <N>      Spaces per indent level (default: 2)
  -t, --use-tabs             Use tabs instead of spaces
  -tc, --trailing-comma <S>  Trailing commas: none|all (default: none)
  -eol, --end-of-line <S>    Line endings: lf|crlf|auto (default: lf)
  -bs, --bracket-spacing <B> Space inside {} in single-line dicts/structs: true|false (default: true)
  -cfg, --config <FILE>      Path to .stashformat config file
  -e, --exclude <GLOB>       Exclude files matching glob (repeatable)
  -h, --help                 Print this help and exit
  -v, --version              Print version and exit";

    internal static int Main(string[] args)
    {
        var options = FormatOptions.Parse(args);

        if (options.ShowHelp)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine(GetVersion());
            return 0;
        }

        // Stdin mode: no paths provided and stdin is redirected
        if (options.Paths.Count == 0 && Console.IsInputRedirected)
        {
            if (options.Write)
            {
                Console.Error.WriteLine("Error: --write cannot be used with stdin.");
                return 2;
            }
            return HandleStdin(options);
        }

        // Default to current directory if no paths given
        if (options.Paths.Count == 0)
        {
            options.Paths.Add(".");
        }

        var runner = new FormatRunner(options);
        FormatResult result;
        try
        {
            result = runner.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }

        bool hasErrors = result.ErrorFiles > 0;

        if (options.Diff)
        {
            foreach (var file in result.Files)
            {
                if (file.Changed && file.Original != null && file.Formatted != null)
                {
                    string relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file.FilePath).Replace('\\', '/');
                    Console.Write(GenerateUnifiedDiff(relativePath, file.Original, file.Formatted));
                }
            }
        }

        if (options.Write)
        {
            foreach (var file in result.Files)
            {
                if (file.Changed && file.Formatted != null)
                {
                    File.WriteAllText(file.FilePath, file.Formatted);
                }
            }
            Console.Error.WriteLine(result.ChangedFiles > 0
                ? $"Formatted {result.TotalFiles} files ({result.ChangedFiles} changed)"
                : $"All {result.TotalFiles} files already formatted");
        }

        if (options.Check)
        {
            foreach (var file in result.Files)
            {
                if (file.Changed)
                {
                    string relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file.FilePath).Replace('\\', '/');
                    Console.WriteLine(relativePath);
                }
            }
            Console.Error.WriteLine(result.ChangedFiles > 0
                ? $"{result.ChangedFiles} of {result.TotalFiles} files need formatting"
                : $"All {result.TotalFiles} files already formatted");
        }

        // Default mode (no --write, --check, --diff): print formatted content to stdout
        if (!options.Write && !options.Check && !options.Diff)
        {
            foreach (var file in result.Files)
            {
                if (file.Formatted != null)
                    Console.Write(file.Formatted);
            }
        }

        if (hasErrors) return 2;
        if (options.Check && result.ChangedFiles > 0) return 1;
        return 0;
    }

    private static int HandleStdin(FormatOptions options)
    {
        string source = Console.In.ReadToEnd();
        string formatted;
        try
        {
            // For stdin, load config from current directory (or the explicit --config path)
            var fileConfig = options.ConfigPath != null
                ? FormatConfig.LoadFromFile(Path.GetFullPath(options.ConfigPath))
                : FormatConfig.Load(Directory.GetCurrentDirectory());

            var config = new FormatConfig
            {
                IndentSize = options.IndentSizeOverride ?? fileConfig.IndentSize,
                UseTabs = options.UseTabsOverride ?? fileConfig.UseTabs,
                TrailingComma = options.TrailingCommaOverride ?? fileConfig.TrailingComma,
                EndOfLine = options.EndOfLineOverride ?? fileConfig.EndOfLine,
                BracketSpacing = options.BracketSpacingOverride ?? fileConfig.BracketSpacing,
            };

            var formatter = new StashFormatter(config);
            formatted = formatter.Format(source);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }

        if (options.Check)
        {
            return string.Equals(source, formatted, StringComparison.Ordinal) ? 0 : 1;
        }

        if (options.Diff && !string.Equals(source, formatted, StringComparison.Ordinal))
        {
            Console.Write(GenerateUnifiedDiff("stdin", source, formatted));
            return 0;
        }

        Console.Write(formatted);
        return 0;
    }

    private static string GenerateUnifiedDiff(string filePath, string original, string formatted)
    {
        const int context = 3;
        string[] oldLines = original.Split('\n');
        string[] newLines = formatted.Split('\n');

        var edits = ComputeEditScript(oldLines, newLines);
        var hunks = BuildHunks(edits, oldLines.Length, newLines.Length, context);

        if (hunks.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"--- a/{filePath}");
        sb.AppendLine($"+++ b/{filePath}");

        foreach (var hunk in hunks)
        {
            int oldCount = hunk.OldCount;
            int newCount = hunk.NewCount;

            string oldRange = oldCount == 1 ? $"{hunk.OldStart}" : $"{hunk.OldStart},{oldCount}";
            string newRange = newCount == 1 ? $"{hunk.NewStart}" : $"{hunk.NewStart},{newCount}";
            sb.AppendLine($"@@ -{oldRange} +{newRange} @@");

            foreach (var line in hunk.Lines)
                sb.AppendLine(line);
        }

        return sb.ToString();
    }

    // Edit operation kind
    private enum EditKind { Keep, Remove, Add }

    private readonly record struct Edit(EditKind Kind, string Line);

    private static List<Edit> ComputeEditScript(string[] oldLines, string[] newLines)
    {
        int m = oldLines.Length;
        int n = newLines.Length;

        var dp = new int[m + 1, n + 1];
        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                dp[i, j] = string.Equals(oldLines[i - 1], newLines[j - 1], StringComparison.Ordinal)
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // Backtrack to produce edit script
        var edits = new List<Edit>(m + n);
        int oi = m, ni = n;
        while (oi > 0 || ni > 0)
        {
            if (oi > 0 && ni > 0 && string.Equals(oldLines[oi - 1], newLines[ni - 1], StringComparison.Ordinal))
            {
                edits.Add(new Edit(EditKind.Keep, oldLines[oi - 1]));
                oi--;
                ni--;
            }
            else if (ni > 0 && (oi == 0 || dp[oi, ni - 1] >= dp[oi - 1, ni]))
            {
                edits.Add(new Edit(EditKind.Add, newLines[ni - 1]));
                ni--;
            }
            else
            {
                edits.Add(new Edit(EditKind.Remove, oldLines[oi - 1]));
                oi--;
            }
        }

        edits.Reverse();
        return edits;
    }

    private sealed class DiffHunk
    {
        public int OldStart;
        public int NewStart;
        public int OldCount;
        public int NewCount;
        public List<string> Lines = new();
    }

    private static List<DiffHunk> BuildHunks(List<Edit> edits, int totalOld, int totalNew, int context)
    {
        var hunks = new List<DiffHunk>();
        int oldLine = 1;
        int newLine = 1;

        // Find all changed regions (indices into edits list)
        // We'll do a single pass: buffer context lines and flush hunks
        DiffHunk? current = null;
        var contextBuffer = new Queue<(Edit edit, int oldPos, int newPos)>();

        void FlushHunk()
        {
            if (current == null) return;
            hunks.Add(current);
            current = null;
        }

        int editIdx = 0;
        while (editIdx < edits.Count)
        {
            var edit = edits[editIdx];
            if (edit.Kind == EditKind.Keep)
            {
                if (current != null)
                {
                    // Trailing context for the current hunk
                    current.Lines.Add($" {edit.Line}");
                    current.OldCount++;
                    current.NewCount++;

                    contextBuffer.Enqueue((edit, oldLine, newLine));
                    if (contextBuffer.Count > context)
                        contextBuffer.Dequeue();

                    // Check if next change is within context distance
                    int lookahead = 1;
                    bool nearNextChange = false;
                    for (int k = editIdx + 1; k < edits.Count && lookahead <= context * 2; k++, lookahead++)
                    {
                        if (edits[k].Kind != EditKind.Keep)
                        {
                            nearNextChange = lookahead <= context;
                            break;
                        }
                    }

                    if (!nearNextChange)
                    {
                        FlushHunk();
                    }
                }
                else
                {
                    contextBuffer.Enqueue((edit, oldLine, newLine));
                    if (contextBuffer.Count > context)
                        contextBuffer.Dequeue();
                }
                oldLine++;
                newLine++;
            }
            else
            {
                if (current == null)
                {
                    current = new DiffHunk();
                    // Drain context buffer as leading context
                    foreach (var (ctxEdit, ctxOld, ctxNew) in contextBuffer)
                    {
                        current.Lines.Add($" {ctxEdit.Line}");
                        current.OldCount++;
                        current.NewCount++;
                    }
                    // OldStart / NewStart: first old/new line in buffer (or current if empty)
                    if (contextBuffer.Count > 0)
                    {
                        var first = contextBuffer.Peek();
                        current.OldStart = first.oldPos;
                        current.NewStart = first.newPos;
                    }
                    else
                    {
                        current.OldStart = oldLine;
                        current.NewStart = newLine;
                    }
                    contextBuffer.Clear();
                }

                if (edit.Kind == EditKind.Remove)
                {
                    current.Lines.Add($"-{edit.Line}");
                    current.OldCount++;
                    oldLine++;
                }
                else // Add
                {
                    current.Lines.Add($"+{edit.Line}");
                    current.NewCount++;
                    newLine++;
                }
            }
            editIdx++;
        }

        FlushHunk();
        return hunks;
    }

    private static string GetVersion()
    {
        var assembly = typeof(Program).Assembly;
        var version = assembly.GetName().Version;
        return version != null ? $"stash-format {version.Major}.{version.Minor}.{version.Build}" : "stash-format 0.1.0";
    }
}
