using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Stash.Cli.PackageManager.Commands;

public static partial class Regexes
{
    [GeneratedRegex(@"[^a-z0-9-]", RegexOptions.IgnoreCase)]
    public static partial Regex InvalidChars();

    [GeneratedRegex(@"^[^a-z]+", RegexOptions.IgnoreCase)]
    public static partial Regex LeadingNonLetters();

    [GeneratedRegex(@"-+$", RegexOptions.IgnoreCase)]
    public static partial Regex TrailingDashes();
}

public static class InitCommand
{
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
    }

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
