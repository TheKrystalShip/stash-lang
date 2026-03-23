using System;
using System.IO;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg update</c> command for upgrading one or all
/// installed dependencies to the latest versions permitted by their constraints.
/// </summary>
/// <remarks>
/// <para>
/// When a package name is provided, only that package's entry is removed from
/// <see cref="LockFile"/> before re-running <see cref="PackageInstaller.Install"/>,
/// allowing the resolver to pick a newer compatible version while leaving all
/// other resolved versions intact.
/// </para>
/// <para>
/// When no package name is given, the entire <c>stash-lock.json</c> file is
/// deleted so that all dependencies are re-resolved from scratch.
/// </para>
/// <para>
/// Registry resolution and authentication use <see cref="RegistryResolver"/>
/// and <see cref="UserConfig"/> respectively.
/// </para>
/// </remarks>
public static class UpdateCommand
{
    /// <summary>
    /// Executes the update command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg update</c>. An optional
    /// positional argument specifies the single package to update; when omitted,
    /// all dependencies are updated. The <c>--registry &lt;url&gt;</c> flag
    /// optionally overrides the default registry.
    /// </param>
    public static void Execute(string[] args)
    {
        string projectDir = Directory.GetCurrentDirectory();

        var manifest = PackageManifest.Load(projectDir);
        if (manifest == null)
        {
            Console.Error.WriteLine("No stash.json found in current directory.");
            Environment.Exit(1);
            return;
        }

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

        if (packageName != null)
        {
            var lockFile = LockFile.Load(projectDir);
            if (lockFile != null)
            {
                lockFile.Resolved.Remove(packageName);
                lockFile.Save(projectDir);
            }
            Console.WriteLine($"Updating {packageName}...");
        }
        else
        {
            string lockPath = Path.Combine(projectDir, "stash-lock.json");
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }

            Console.WriteLine("Updating all dependencies...");
        }

        try
        {
            var (registryUrl, _) = RegistryResolver.Resolve(args);
            var config = UserConfig.Load();
            var source = new RegistryClient(registryUrl, config.GetToken(registryUrl));
            PackageInstaller.Install(projectDir, source);
            Console.WriteLine("Dependencies updated.");
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine("A package registry is required to re-resolve dependencies.");
            Console.Error.WriteLine("Lock file has been cleared. Dependencies will be re-resolved on next install with a registry.");
        }
    }
}
