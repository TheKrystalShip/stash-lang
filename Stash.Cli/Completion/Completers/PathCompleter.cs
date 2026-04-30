using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Stash.Cli.Completion.Completers;

/// <summary>
/// Completes file-system paths in shell mode (argument positions, redirects, quoted strings).
/// <para>
/// Behavior:
/// <list type="bullet">
///   <item>Bare <c>~</c> completes to <c>~/</c> (<see cref="CandidateKind.Directory"/>).</item>
///   <item><c>~/…</c> expands the home directory for lookup but preserves <c>~/</c> in output.</item>
///   <item>Dotfiles are hidden unless the name part starts with <c>.</c>.</item>
///   <item>Directories get a trailing <c>/</c> in display and insert.</item>
///   <item>Permission errors and I/O exceptions are swallowed; partial results are returned.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class PathCompleter : ICompleter
{
    public IReadOnlyList<Candidate> Complete(CursorContext ctx, CompletionDeps deps)
    {
        string token = ctx.TokenText;

        // Bare tilde → complete to ~/
        if (token == "~")
            return [new Candidate("~/", "~/", CandidateKind.Directory)];

        // Resolve tilde prefix
        string? tildeHome = null;
        string resolvedToken = token;
        if (token.StartsWith("~/", StringComparison.Ordinal))
        {
            tildeHome = GetHomeDirectory();
            if (tildeHome != null)
                resolvedToken = tildeHome + token.Substring(1); // replace ~ with home
        }

        // Split into dirPart + namePart at last /
        int lastSlash = resolvedToken.LastIndexOf('/');
        string dirPart;
        string namePart;
        string originalDirPart; // the user-typed directory prefix (for insert)

        if (lastSlash < 0)
        {
            // No slash — complete in current directory
            dirPart = ".";
            namePart = resolvedToken;
            originalDirPart = string.Empty;
        }
        else
        {
            dirPart = lastSlash == 0 ? "/" : resolvedToken.Substring(0, lastSlash);
            namePart = resolvedToken.Substring(lastSlash + 1);

            // Compute the user-visible dir prefix to prepend to insert values.
            // If there was a tilde rewrite, the user typed e.g. "~/foo/" — restore that.
            if (tildeHome != null && token.StartsWith("~/", StringComparison.Ordinal))
            {
                int userLastSlash = token.LastIndexOf('/');
                originalDirPart = userLastSlash < 0 ? string.Empty : token.Substring(0, userLastSlash + 1);
            }
            else
            {
                originalDirPart = resolvedToken.Substring(0, lastSlash + 1);
            }
        }

        // Resolve the directory to an absolute path
        string absDir;
        try
        {
            absDir = Path.GetFullPath(string.IsNullOrEmpty(dirPart) ? "." : dirPart);
        }
        catch
        {
            return [];
        }

        if (!Directory.Exists(absDir))
            return [];

        var candidates = new List<Candidate>();

        try
        {
            foreach (string fullPath in Directory.EnumerateFileSystemEntries(absDir))
            {
                string entryName = Path.GetFileName(fullPath);

                // Dotfile rule: skip dotfiles unless namePart starts with '.'
                if (entryName.StartsWith('.') && !namePart.StartsWith('.'))
                    continue;

                // Pre-filter by smart-case prefix for performance
                if (!SmartCaseMatcher.Matches(namePart, entryName))
                    continue;

                bool isDir = Directory.Exists(fullPath);
                string display = entryName + (isDir ? "/" : string.Empty);
                string insert = originalDirPart + entryName + (isDir ? "/" : string.Empty);
                CandidateKind kind = isDir ? CandidateKind.Directory : CandidateKind.File;

                candidates.Add(new Candidate(display, insert, kind));
            }
        }
        catch (UnauthorizedAccessException) { /* swallow, return partial */ }
        catch (DirectoryNotFoundException) { /* swallow */ }
        catch (IOException) { /* swallow */ }

        return candidates;
    }

    private static string? GetHomeDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Environment.GetEnvironmentVariable("USERPROFILE");
        return Environment.GetEnvironmentVariable("HOME");
    }
}
