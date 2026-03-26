using System;
using System.Text.Json;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg info</c> command for displaying detailed metadata
/// about a package retrieved from the registry.
/// </summary>
/// <remarks>
/// <para>
/// Prints the package name, latest version, description, license, repository URL,
/// owner list, published version history, and (up to ten lines of) the README
/// to standard output.
/// </para>
/// </remarks>
public static class InfoCommand
{
    /// <summary>
    /// Executes the info command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg info</c>.  The first
    /// positional argument is the package name (required).  The
    /// <c>--registry &lt;url&gt;</c> flag optionally overrides the default registry.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when no package name is supplied.
    /// </exception>
    public static void Execute(string[] args)
    {
        string? packageName = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--registry" && i + 1 < args.Length)
            {
                i++;
            }
            else if (packageName == null)
            {
                packageName = args[i];
            }
        }

        if (packageName == null)
        {
            throw new ArgumentException("Usage: stash pkg info <package-name>");
        }

        var (registryUrl, _) = RegistryResolver.Resolve(args);
        var config = UserConfig.Load();
        var entry = config.GetEntry(registryUrl);
        var client = new RegistryClient(registryUrl, entry?.Token, entry?.RefreshToken,
            entry?.ExpiresAt, entry?.MachineId, registryUrl);

        string? info = client.GetPackageInfo(packageName);
        if (info == null)
        {
            Console.Error.WriteLine($"Package '{packageName}' not found.");
            Environment.Exit(1);
            return;
        }

        using var doc = JsonDocument.Parse(info);
        var root = doc.RootElement;

        Console.WriteLine($"{GetString(root, "name")}");
        Console.WriteLine($"Latest: {GetString(root, "latest")}");
        if (root.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
        {
            Console.WriteLine($"Description: {desc.GetString()}");
        }

        if (root.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.String)
        {
            Console.WriteLine($"License: {lic.GetString()}");
        }

        if (root.TryGetProperty("repository", out var repo) && repo.ValueKind == JsonValueKind.String)
        {
            Console.WriteLine($"Repository: {repo.GetString()}");
        }

        if (root.TryGetProperty("owners", out var owners) && owners.ValueKind == JsonValueKind.Array)
        {
            Console.Write("Owners: ");
            bool first = true;
            foreach (var o in owners.EnumerateArray())
            {
                if (!first)
                {
                    Console.Write(", ");
                }

                Console.Write(o.GetString());
                first = false;
            }
            Console.WriteLine();
        }

        if (root.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Object)
        {
            Console.WriteLine();
            Console.WriteLine("Versions:");
            foreach (var v in versions.EnumerateObject())
            {
                string publishedAt = "";
                if (v.Value.TryGetProperty("publishedAt", out var pa))
                {
                    publishedAt = pa.GetString() ?? "";
                }

                Console.WriteLine($"  {v.Name,-15} {publishedAt}");
            }
        }

        // Show README excerpt if present
        if (root.TryGetProperty("readme", out var readme) && readme.ValueKind == JsonValueKind.String)
        {
            string readmeText = readme.GetString() ?? "";
            if (readmeText.Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine("README:");
                // Show first 10 lines
                string[] lines = readmeText.Split('\n');
                int maxLines = Math.Min(10, lines.Length);
                for (int i = 0; i < maxLines; i++)
                {
                    Console.WriteLine($"  {lines[i]}");
                }

                if (lines.Length > maxLines)
                {
                    Console.WriteLine($"  ... ({lines.Length - maxLines} more lines)");
                }
            }
        }
    }

    /// <summary>
    /// Reads a string property from a <see cref="JsonElement"/>, returning an empty
    /// string when the property is absent or not a JSON string.
    /// </summary>
    /// <param name="el">The JSON element to query.</param>
    /// <param name="prop">The name of the property to read.</param>
    /// <returns>The string value of the property, or an empty string if not found.</returns>
    private static string GetString(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString() ?? "";
        }

        return "";
    }
}
