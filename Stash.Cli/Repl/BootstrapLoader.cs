namespace Stash.Cli.Repl;

using System;
using System.IO;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;

/// <summary>
/// Loads the extracted prompt bootstrap scripts into the REPL VM in the
/// correct dependency order.
/// </summary>
/// <remarks>
/// Load order (each file depends on everything before it):
/// <list type="number">
///   <item><description><c>palette.stash</c> — defines the <c>Palette</c> struct</description></item>
///   <item><description><c>themes/*.stash</c> — registers palettes via <c>prompt.themeRegister</c></description></item>
///   <item><description><c>bootstrap.stash</c> — defines the <c>theme</c> and <c>starter</c> globals and activates the default theme</description></item>
///   <item><description><c>starters/*.stash</c> — registers starter fns via <c>starter.register</c> (defined in bootstrap.stash)</description></item>
///   <item><description><c>default-prompt.stash</c> — registers the default prompt fn via <c>prompt.set</c></description></item>
/// </list>
/// Each file is guarded independently: a load failure emits a warning and
/// processing continues. The prompt renderer's <c>"stash> "</c> fallback
/// activates automatically if the default fn was never registered.
/// </remarks>
internal static class BootstrapLoader
{
    /// <summary>
    /// Loads all prompt bootstrap files from <paramref name="bootstrapDir"/>
    /// into <paramref name="vm"/> in dependency order.
    /// </summary>
    /// <param name="bootstrapDir">
    /// The directory containing extracted prompt resources
    /// (<c>~/.config/stash/prompt</c>).
    /// </param>
    /// <param name="vm">The active REPL virtual machine.</param>
    public static void Load(string bootstrapDir, VirtualMachine vm)
    {
        // Dependency-ordered load list. Themes must precede bootstrap.stash
        // (which activates the default theme), and starters must follow
        // bootstrap.stash (which defines the `starter` global they rely on).
        string[] loadOrder =
        [
            "palette.stash",
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
            "bootstrap.stash",
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
            "default-prompt.stash",
        ];

        foreach (string relPath in loadOrder)
        {
            string fullPath = Path.Combine(
                bootstrapDir,
                relPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                Console.Error.WriteLine($"prompt: bootstrap file missing: {relPath}");
                continue;
            }

            try
            {
                string source = File.ReadAllText(fullPath);
                ShellRunner.EvaluateSource(source, vm);
            }
            catch (RuntimeError ex)
            {
                Console.Error.WriteLine($"prompt: bootstrap failed loading '{relPath}': {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"prompt: bootstrap failed loading '{relPath}': {ex.Message}");
            }
        }
    }
}
