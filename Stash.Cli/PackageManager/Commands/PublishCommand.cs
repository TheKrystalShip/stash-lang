using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

public static class PublishCommand
{
    public static void Execute(string[] args)
    {
        var (registryUrl, _) = RegistryResolver.Resolve(args);

        // Load and validate manifest
        string manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "stash.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("No stash.json found. Run 'stash pkg init' first.");
        }

        string json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize(json, PackageManifestJsonContext.Default.PackageManifest);
        if (manifest == null)
        {
            throw new InvalidOperationException("Invalid stash.json.");
        }

        if (string.IsNullOrEmpty(manifest.Name))
        {
            throw new InvalidOperationException("Package name is required in stash.json.");
        }

        if (string.IsNullOrEmpty(manifest.Version))
        {
            throw new InvalidOperationException("Package version is required in stash.json.");
        }

        if (manifest.Private == true)
        {
            throw new InvalidOperationException("Cannot publish a private package.");
        }

        // Get auth token
        var config = UserConfig.Load();
        string? token = config.GetToken(registryUrl);
        if (token == null)
        {
            throw new InvalidOperationException($"Not logged in to registry '{registryUrl}'. Run 'stash pkg login'.");
        }

        // Pack the tarball
        string tempDir = Path.Combine(Path.GetTempPath(), $"stash-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tarballPath = Path.Combine(tempDir, $"{manifest.Name}-{manifest.Version}.tar.gz");

        try
        {
            var files = Tarball.Pack(Directory.GetCurrentDirectory(), tarballPath);
            Console.WriteLine($"Packed {files.Count} files into tarball.");

            // Compute integrity
            byte[] tarballBytes = File.ReadAllBytes(tarballPath);
            byte[] hash = SHA256.HashData(tarballBytes);
            string integrity = "sha256-" + Convert.ToBase64String(hash);

            // Publish
            var client = new RegistryClient(registryUrl, token);
            using var stream = new FileStream(tarballPath, FileMode.Open, FileAccess.Read);
            client.Publish(stream, integrity);

            Console.WriteLine($"Published {manifest.Name}@{manifest.Version} to {registryUrl}.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
