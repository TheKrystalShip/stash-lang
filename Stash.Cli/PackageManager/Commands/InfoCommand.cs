using System;
using System.Text.Json;

namespace Stash.Cli.PackageManager.Commands;

public static class InfoCommand
{
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
        var client = new RegistryClient(registryUrl, config.GetToken(registryUrl));

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

    private static string GetString(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString() ?? "";
        }

        return "";
    }
}
