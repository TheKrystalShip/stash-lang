using System;
using System.IO;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements the <c>stash pkg uninstall</c> command for removing a package
/// from the current project.
/// </summary>
/// <remarks>
/// <para>
/// Delegates to <see cref="PackageInstaller.UninstallPackage"/>, which removes
/// the package directory from <c>stash_packages/</c>, strips the corresponding
/// entry from <c>stash.json</c>'s <c>dependencies</c> section, and updates
/// <c>stash-lock.json</c> accordingly.
/// </para>
/// </remarks>
public static class UninstallCommand
{
    /// <summary>
    /// Executes the uninstall command with the given arguments.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments following <c>stash pkg uninstall</c>. The first
    /// positional argument is the name of the package to remove (required).
    /// </param>
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
