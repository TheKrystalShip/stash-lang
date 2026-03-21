using System;
using System.IO;

namespace Stash.Cli.PackageManager.Commands;

public static class UninstallCommand
{
    public static void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: stash pkg uninstall <name>");
            Environment.Exit(64);
            return;
        }

        string projectDir = Directory.GetCurrentDirectory();
        string packageName = args[0];

        PackageInstaller.UninstallPackage(projectDir, packageName);
        Console.WriteLine($"Removed {packageName}.");
    }
}
