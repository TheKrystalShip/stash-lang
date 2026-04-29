namespace Stash.Cli.Repl;

using System;
using System.IO;
using System.Linq;
using System.Reflection;

/// <summary>
/// Extracts the bundled prompt bootstrap scripts from embedded resources to
/// <c>~/.config/stash/prompt/</c> on first run and after version upgrades.
/// </summary>
/// <remarks>
/// Embedded resource names follow the pattern
/// <c>Stash.Cli.Resources.prompt.{relative-path-with-dots}</c>.
/// The known file list is hardcoded to avoid round-tripping the resource
/// manifest name back to a file path (which is ambiguous when filenames
/// contain dots).
/// </remarks>
internal static class BootstrapExtractor
{
    private const string _prefix = "Stash.Cli.Resources.prompt.";

    /// <summary>All prompt resource files in their canonical relative path form.</summary>
    private static readonly string[] _files =
    [
        "VERSION",
        "palette.stash",
        "bootstrap.stash",
        "default-prompt.stash",
        "themes/default.stash",
        "themes/nord.stash",
        "themes/catppuccin-mocha.stash",
        "themes/catppuccin-latte.stash",
        "themes/monokai.stash",
        "themes/dracula.stash",
        "themes/gruvbox-dark.stash",
        "themes/gruvbox-light.stash",
        "themes/tokyo-night.stash",
        "themes/solarized-dark.stash",
        "themes/solarized-light.stash",
        "themes/one-dark.stash",
        "themes/rose-pine.stash",
        "themes/synthwave.stash",
        "themes/ayu-dark.stash",
        "themes/github-light.stash",
        "themes/monochrome.stash",
        "starters/minimal.stash",
        "starters/bash-classic.stash",
        "starters/pure.stash",
        "starters/developer.stash",
        "starters/pwsh-style.stash",
        "starters/powerline-lite.stash",
        "starters/arrow.stash",
        "starters/two-line.stash",
        "starters/robbyrussell.stash",
        "starters/fish-style.stash",
        "starters/bracket.stash",
        "starters/compact.stash",
        "starters/status.stash",
        "starters/emoji.stash",
    ];

    /// <summary>
    /// Returns <c>~/.config/stash/prompt</c> — the directory where prompt
    /// bootstrap scripts are extracted.
    /// </summary>
    public static string GetTargetDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "stash", "prompt");

    /// <summary>
    /// Returns <see langword="true"/> if extraction is required: the target
    /// directory is missing, the <c>VERSION</c> file is absent, or the on-disk
    /// version differs from the embedded version.
    /// </summary>
    public static bool NeedsExtraction(string targetDir, string embeddedVersion)
    {
        if (!Directory.Exists(targetDir)) return true;
        string versionFile = Path.Combine(targetDir, "VERSION");
        if (!File.Exists(versionFile)) return true;
        string diskVersion = File.ReadAllText(versionFile).Trim();
        return !string.Equals(diskVersion, embeddedVersion, StringComparison.Ordinal);
    }

    /// <summary>
    /// Wipes <paramref name="targetDir"/>, recreates it, and writes all
    /// embedded prompt resource files. Emits warnings to stderr on partial
    /// failure rather than throwing — callers fall back to the <c>"stash> "</c>
    /// default prompt.
    /// </summary>
    public static void Extract(string targetDir)
    {
        try
        {
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
            Directory.CreateDirectory(targetDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"prompt: could not prepare bootstrap directory '{targetDir}': {ex.Message}");
            return;
        }

        var asm = typeof(BootstrapExtractor).Assembly;

        foreach (string relPath in _files)
        {
            string manifestName = RelPathToManifestName(relPath);
            using Stream? stream = asm.GetManifestResourceStream(manifestName);
            if (stream is null)
            {
                Console.Error.WriteLine($"prompt: embedded resource not found: {manifestName}");
                continue;
            }

            string diskPath = Path.Combine(
                targetDir,
                relPath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                string? dir = Path.GetDirectoryName(diskPath);
                if (dir is not null)
                    Directory.CreateDirectory(dir);

                using FileStream fs = File.Create(diskPath);
                stream.CopyTo(fs);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"prompt: could not write '{relPath}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Reads the embedded <c>VERSION</c> and extracts files to
    /// <see cref="GetTargetDir()"/> if the on-disk version is missing or
    /// outdated.
    /// </summary>
    public static void EnsureExtracted()
    {
        string targetDir = GetTargetDir();
        string embeddedVersion = ReadEmbeddedVersion();
        if (NeedsExtraction(targetDir, embeddedVersion))
            Extract(targetDir);
    }

    // ---------------------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Converts a relative resource path (e.g. <c>"themes/nord.stash"</c>) to
    /// the embedded manifest resource name.
    /// </summary>
    private static string RelPathToManifestName(string relPath) =>
        _prefix + relPath.Replace('/', '.').Replace('\\', '.');

    /// <summary>Reads the embedded VERSION file and returns its trimmed content.</summary>
    private static string ReadEmbeddedVersion()
    {
        var asm = typeof(BootstrapExtractor).Assembly;
        using Stream? stream = asm.GetManifestResourceStream(RelPathToManifestName("VERSION"));
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
