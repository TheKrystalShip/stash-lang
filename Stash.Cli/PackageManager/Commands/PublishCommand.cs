using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg publish</c> command for packing and uploading the
/// current project to the registry.
/// </summary>
/// <remarks>
/// <para>
/// Validates <c>stash.json</c> (name, version, non-private flag), packs the
/// project into a temporary <c>.tar.gz</c> via <see cref="Tarball.Pack"/>, computes
/// a SHA-256 integrity hash, and uploads the tarball using
/// <see cref="RegistryClient.Publish"/>.
/// </para>
/// <para>
/// An authenticated session is required.  The token is read from
/// <see cref="UserConfig"/> for the resolved registry URL.
/// </para>
/// </remarks>
public static class PublishCommand
{
    /// <summary>
    /// Executes the publish command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg publish</c>.  The
    /// <c>--registry &lt;url&gt;</c> flag optionally overrides the default registry.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>stash.json</c> is missing, invalid, marks the package as
    /// private, or when the user is not logged in to the target registry.
    /// </exception>
    public static void Execute(string[] args)
    {
        string? cliToken = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--token" && i + 1 < args.Length)
            {
                cliToken = args[++i];
            }
        }
        string registryUrl = UserConfig.ResolveRegistryUrl(RegistryResolver.ParseRegistryFlag(args));

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

        // Get auth token — priority: --token flag > STASH_TOKEN env var > config file
        string? token = cliToken ?? Environment.GetEnvironmentVariable("STASH_TOKEN");
        RegistryClient client;
        if (!string.IsNullOrEmpty(token))
        {
            client = new RegistryClient(registryUrl, token);
        }
        else
        {
            var config = UserConfig.Load();
            var entry = config.GetEntry(registryUrl);
            if (entry?.Token == null)
            {
                throw new InvalidOperationException(
                    $"Not logged in to registry '{registryUrl}'. Run 'stash pkg login', set the STASH_TOKEN environment variable, or use --token.");
            }
            client = new RegistryClient(registryUrl, entry.Token, entry.RefreshToken,
                entry.ExpiresAt, entry.MachineId, registryUrl);
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
