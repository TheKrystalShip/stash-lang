using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg install</c> command for installing packages from
/// the registry or Git sources.
/// </summary>
/// <remarks>
/// <para>
/// When invoked with no positional arguments the command installs all dependencies
/// declared in <c>stash.json</c> using <see cref="PackageInstaller.Install"/>.
/// </para>
/// <para>
/// When a package specifier is provided it is parsed as either a Git source URL
/// (prefixed with <c>git:</c>), a versioned registry reference
/// (<c>&lt;name&gt;@&lt;version&gt;</c>), or a bare package name.  The dependency
/// is written to <c>stash.json</c> via <see cref="AddDependency"/> before the full
/// install is triggered.
/// </para>
/// </remarks>
public static class InstallCommand
{
    /// <summary>
    /// Executes the install command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg install</c>.  An optional
    /// positional argument specifies a package specifier to add (e.g.
    /// <c>my-package</c>, <c>my-package@1.2.0</c>, or
    /// <c>git:https://github.com/…#main</c>).  The <c>--registry &lt;url&gt;</c>
    /// flag optionally overrides the default registry.
    /// </param>
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

    /// <summary>
    /// Adds or updates the <paramref name="name"/> entry in the <c>dependencies</c>
    /// object of the project's <c>stash.json</c> file.
    /// </summary>
    /// <param name="projectDir">The root directory of the project containing <c>stash.json</c>.</param>
    /// <param name="name">The package name to add or update.</param>
    /// <param name="constraint">The version constraint string to store (e.g. <c>^1.0.0</c> or <c>*</c>).</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>stash.json</c> does not exist or cannot be parsed.
    /// </exception>
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

    /// <summary>
    /// Derives a lower-case package name from the final path segment of a Git
    /// repository URL, stripping any trailing <c>.git</c> suffix.
    /// </summary>
    /// <param name="url">The Git repository URL to extract the name from.</param>
    /// <returns>
    /// A lower-case string representing the repository name, suitable for use as
    /// a Stash package name.
    /// </returns>
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
