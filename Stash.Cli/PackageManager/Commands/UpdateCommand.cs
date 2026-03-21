using System;
using System.IO;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

public static class UpdateCommand
{
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
