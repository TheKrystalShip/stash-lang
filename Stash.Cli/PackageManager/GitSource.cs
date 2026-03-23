using System;
using System.Diagnostics;
using Stash.Common;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Provides utilities for identifying, parsing, and cloning Git-based package
/// sources specified using the <c>git:&lt;url&gt;[#ref]</c> constraint syntax.
/// </summary>
/// <remarks>
/// <para>
/// A Git source constraint takes the form <c>git:https://github.com/org/repo#main</c>,
/// where the fragment after <c>#</c> is an optional Git ref (branch, tag, or commit
/// SHA).  When no ref is specified the repository's default branch is used.
/// </para>
/// </remarks>
public static class GitSource
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="constraint"/> is a Git source
    /// specifier (i.e. it begins with the <c>git:</c> prefix).
    /// </summary>
    /// <param name="constraint">The dependency constraint string to test.</param>
    /// <returns>
    /// <c>true</c> when <paramref name="constraint"/> starts with <c>git:</c>;
    /// otherwise <c>false</c>.
    /// </returns>
    public static bool IsGitSource(string constraint)
    {
        return constraint.StartsWith("git:", StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses a <c>git:&lt;url&gt;[#ref]</c> constraint into its URL and optional
    /// Git ref components.
    /// </summary>
    /// <param name="constraint">A Git source constraint string prefixed with <c>git:</c>.</param>
    /// <returns>
    /// A tuple of (<c>url</c>, <c>gitRef</c>) where <c>gitRef</c> is <c>null</c>
    /// when no <c>#ref</c> fragment is present.
    /// </returns>
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

    /// <summary>
    /// Clones a Git repository into <paramref name="targetDir"/> and checks out the
    /// specified ref, if any.
    /// </summary>
    /// <param name="url">The Git remote URL to clone.</param>
    /// <param name="gitRef">
    /// The branch, tag, or commit SHA to check out after cloning, or <c>null</c> to
    /// keep the default branch.
    /// </param>
    /// <param name="targetDir">
    /// The local directory to clone into.  Must not already exist or must be empty.
    /// </param>
    /// <returns>The <paramref name="targetDir"/> path (for convenience).</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>git clone</c> or <c>git checkout</c> exits with a non-zero
    /// exit code.
    /// </exception>
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

    /// <summary>
    /// Loads the <see cref="PackageManifest"/> from a cloned repository directory.
    /// </summary>
    /// <param name="repoDir">The root directory of the cloned repository.</param>
    /// <returns>
    /// The parsed <see cref="PackageManifest"/>, or <c>null</c> when no
    /// <c>stash.json</c> is found in <paramref name="repoDir"/>.
    /// </returns>
    public static PackageManifest? GetManifest(string repoDir)
    {
        return PackageManifest.Load(repoDir);
    }

    /// <summary>
    /// Runs a <c>git</c> sub-command as a child process and throws if it exits
    /// with a non-zero code.
    /// </summary>
    /// <param name="args">The argument list to pass to the <c>git</c> executable.</param>
    /// <param name="workingDir">
    /// The working directory for the process, or <c>null</c> to inherit the current
    /// directory.
    /// </param>
    /// <param name="errorPrefix">
    /// A human-readable prefix prepended to the process's stderr output when
    /// constructing the exception message.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the git process exits with a non-zero exit code.
    /// </exception>
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
        {
            throw new InvalidOperationException($"{errorPrefix}{stderr.Trim()}");
        }
    }
}
