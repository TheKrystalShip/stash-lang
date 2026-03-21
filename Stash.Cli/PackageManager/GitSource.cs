using System;
using System.Diagnostics;
using Stash.Common;

namespace Stash.Cli.PackageManager;

public static class GitSource
{
    public static bool IsGitSource(string constraint)
    {
        return constraint.StartsWith("git:", StringComparison.Ordinal);
    }

    public static (string Url, string? Ref) ParseGitSource(string constraint)
    {
        string withoutPrefix = constraint.Substring("git:".Length);
        int hashIndex = withoutPrefix.IndexOf('#');
        if (hashIndex < 0)
        {
            return (withoutPrefix, null);
        }

        string url = withoutPrefix.Substring(0, hashIndex);
        string gitRef = withoutPrefix.Substring(hashIndex + 1);
        return (url, gitRef.Length > 0 ? gitRef : null);
    }

    public static string CloneAndCheckout(string url, string? gitRef, string targetDir)
    {
        RunGit(["clone", url, targetDir], workingDir: null,
            $"Failed to clone git repository '{url}': ");

        if (gitRef is not null)
        {
            RunGit(["checkout", gitRef], workingDir: targetDir,
                $"Failed to checkout ref '{gitRef}' in '{targetDir}': ");
        }

        return targetDir;
    }

    public static PackageManifest? GetManifest(string repoDir)
    {
        return PackageManifest.Load(repoDir);
    }

    private static void RunGit(string[] args, string? workingDir, string errorPrefix)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (workingDir is not null)
        {
            psi.WorkingDirectory = workingDir;
        }

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{errorPrefix}{stderr.Trim()}");
    }
}
