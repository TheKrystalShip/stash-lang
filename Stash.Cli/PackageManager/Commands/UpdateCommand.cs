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

        string? packageName = args.Length > 0 ? args[0] : null;

        if (packageName != null)
        {
            // Update specific package: remove from lock and re-install
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
            // Update all: delete lock file entirely
            string lockPath = Path.Combine(projectDir, "stash-lock.json");
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }

            Console.WriteLine("Updating all dependencies...");
        }

        try
        {
            PackageInstaller.Install(projectDir);
            Console.WriteLine("Dependencies updated.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("package source is required"))
        {
            Console.Error.WriteLine("A package registry is required to re-resolve dependencies.");
            Console.Error.WriteLine("Lock file has been cleared. Dependencies will be re-resolved on next install with a registry.");
        }
    }
}
