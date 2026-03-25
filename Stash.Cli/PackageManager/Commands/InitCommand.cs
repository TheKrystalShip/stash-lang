using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Provides source-generated regular expressions used by <see cref="InitCommand"/>
/// to sanitise directory names into valid Stash package names.
/// </summary>
public static partial class Regexes
{
    /// <summary>
    /// Matches any character that is not a lower-case ASCII letter, digit, or hyphen,
    /// used to replace invalid characters with hyphens.
    /// </summary>
    [GeneratedRegex(@"[^a-z0-9-]", RegexOptions.Compiled)]
    public static partial Regex InvalidChars();

    /// <summary>
    /// Matches one or more leading characters that are not lower-case ASCII letters,
    /// used to strip non-letter prefixes from a candidate package name.
    /// </summary>
    [GeneratedRegex(@"^[^a-z]+", RegexOptions.Compiled)]
    public static partial Regex LeadingNonLetters();

    /// <summary>
    /// Matches one or more trailing hyphens, used to strip dangling hyphens from a
    /// candidate package name.
    /// </summary>
    [GeneratedRegex(@"-+$", RegexOptions.Compiled)]
    public static partial Regex TrailingDashes();
}

/// <summary>
/// Implements the <c>stash pkg init</c> command for creating a new <c>stash.json</c>
/// manifest file in the current directory.
/// </summary>
/// <remarks>
/// <para>
/// When the <c>--yes</c> (<c>-y</c>) flag is provided all prompts are skipped and
/// sensible defaults are applied automatically.  Otherwise the user is prompted
/// interactively for each field.
/// </para>
/// <para>
/// The suggested package name is derived from the current directory name by
/// lower-casing it and stripping characters that are invalid in a Stash package name
/// using the <see cref="Regexes"/> helpers.
/// </para>
/// </remarks>
public static class InitCommand
{
    /// <summary>
    /// Executes the init command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg init</c>.  Pass <c>--yes</c>
    /// or <c>-y</c> to accept all defaults without interactive prompts.
    /// </param>
    public static void Execute(string[] args)
    {
        bool useDefaults = false;
        foreach (string arg in args)
        {
            if (arg is "--yes" or "-y")
            {
                useDefaults = true;
            }
        }

        string projectDir = Directory.GetCurrentDirectory();
        string manifestPath = Path.Combine(projectDir, "stash.json");

        if (File.Exists(manifestPath))
        {
            Console.Error.WriteLine("stash.json already exists in this directory.");
            Environment.Exit(1);
            return;
        }

        string dirName = Path.GetFileName(projectDir) ?? "my-package";
        // Sanitize directory name to valid package name
        string defaultName = dirName.ToLowerInvariant();
        defaultName = Regexes.InvalidChars().Replace(defaultName, "-");
        defaultName = Regexes.LeadingNonLetters().Replace(defaultName, "");
        defaultName = Regexes.TrailingDashes().Replace(defaultName, "");
        if (string.IsNullOrEmpty(defaultName))
        {
            defaultName = "my-package";
        }

        string name;
        string version;
        string description;
        string author;
        string license;
        string main;

        if (useDefaults)
        {
            name = defaultName;
            version = "1.0.0";
            description = "";
            author = "";
            license = "MIT";
            main = "index.stash";
        }
        else
        {
            name = Prompt($"name ({defaultName}): ", defaultName);
            version = Prompt("version (1.0.0): ", "1.0.0");
            description = Prompt("description: ", "");
            author = Prompt("author: ", "");
            license = Prompt("license (MIT): ", "MIT");
            main = Prompt("main (index.stash): ", "index.stash");
        }

        var root = new JsonObject
        {
            ["name"] = name,
            ["version"] = version
        };

        if (!string.IsNullOrEmpty(description))
        {
            root["description"] = description;
        }

        if (!string.IsNullOrEmpty(author))
        {
            root["author"] = author;
        }

        if (!string.IsNullOrEmpty(license))
        {
            root["license"] = license;
        }

        root["main"] = main;

        var options = new JsonSerializerOptions { WriteIndented = true, IndentSize = 2 };
        string json = root.ToJsonString(options);
        File.WriteAllText(manifestPath, json + "\n");

        Console.WriteLine($"Created {manifestPath}");

        // Generate .stashignore if it doesn't exist
        string ignorePath = Path.Combine(projectDir, ".stashignore");
        if (!File.Exists(ignorePath))
        {
            const string defaultIgnore =
                """
                # Dependencies
                stashes/

                # Lock file
                stash-lock.json

                # Environment files
                .env
                .env.*

                # Version control
                .git/

                # IDE / editor directories
                .vscode/
                .idea/

                # OS files
                .DS_Store
                Thumbs.db

                # Build output
                *.exe
                *.dll
                """;
            File.WriteAllText(ignorePath, defaultIgnore + "\n");
            Console.WriteLine($"Created {ignorePath}");
        }
    }

    /// <summary>
    /// Writes a prompt to standard output, reads a line from standard input, and
    /// returns the trimmed input or <paramref name="defaultValue"/> when the input
    /// is blank.
    /// </summary>
    /// <param name="prompt">The prompt text to display to the user.</param>
    /// <param name="defaultValue">The value to return when the user enters nothing.</param>
    /// <returns>The trimmed user input, or <paramref name="defaultValue"/> if blank.</returns>
    private static string Prompt(string prompt, string defaultValue)
    {
        Console.Write(prompt);
        string? input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultValue;
        }

        return input.Trim();
    }
}
