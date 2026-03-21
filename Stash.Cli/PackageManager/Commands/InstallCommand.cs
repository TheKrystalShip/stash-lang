using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

public static class InstallCommand
{
    public static void Execute(string[] args)
    {
        string projectDir = Directory.GetCurrentDirectory();

        var positionalArgs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--registry" && i + 1 < args.Length)
            {
                i++;
            }
            else
            {
                positionalArgs.Add(args[i]);
            }
        }

        if (positionalArgs.Count == 0)
        {
            var (registryUrl, _) = RegistryResolver.Resolve(args);
            var config = UserConfig.Load();
            var source = new RegistryClient(registryUrl, config.GetToken(registryUrl));
            PackageInstaller.Install(projectDir, source);
            Console.WriteLine("Dependencies installed.");
            return;
        }

        string specifier = positionalArgs[0];
        string name;
        string constraint;

        if (GitSource.IsGitSource(specifier))
        {
            var (url, _) = GitSource.ParseGitSource(specifier);
            name = ExtractNameFromGitUrl(url);
            constraint = specifier;
        }
        else
        {
            int atIndex = specifier.LastIndexOf('@');
            if (atIndex > 0)
            {
                name = specifier.Substring(0, atIndex);
                string versionPart = specifier.Substring(atIndex + 1);
                if (SemVer.TryParse(versionPart, out _))
                {
                    constraint = $"^{versionPart}";
                }
                else
                {
                    constraint = versionPart;
                }
            }
            else
            {
                name = specifier;
                constraint = "*";
            }
        }

        AddDependency(projectDir, name, constraint);
        Console.WriteLine($"Added {name}@{constraint} to stash.json.");

        try
        {
            var (registryUrl, _) = RegistryResolver.Resolve(args);
            var config = UserConfig.Load();
            var source = new RegistryClient(registryUrl, config.GetToken(registryUrl));
            PackageInstaller.Install(projectDir, source);
            Console.WriteLine("Dependencies installed.");
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("Run 'stash pkg install --registry <url>' to install dependencies.");
        }
    }

    private static void AddDependency(string projectDir, string name, string constraint)
    {
        string path = Path.Combine(projectDir, "stash.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("No stash.json found. Run 'stash pkg init' first.");
        }

        string json = File.ReadAllText(path);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Malformed stash.json.");

        var deps = root["dependencies"]?.AsObject() ?? new JsonObject();
        deps[name] = constraint;
        root["dependencies"] = deps;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, root.ToJsonString(options) + "\n");
    }

    private static string ExtractNameFromGitUrl(string url)
    {
        string lastSegment = url;
        int lastSlash = url.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            lastSegment = url.Substring(lastSlash + 1);
        }

        if (lastSegment.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            lastSegment = lastSegment.Substring(0, lastSegment.Length - 4);
        }

        return lastSegment.ToLowerInvariant();
    }
}
