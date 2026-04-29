using System.Collections.Generic;
using Stash.Bytecode;

namespace Stash.Cli.Shell;

/// <summary>
/// Runtime context threaded through all shell-mode components.
/// </summary>
internal sealed class ShellContext
{
    /// <summary>The active REPL virtual machine. Used for ${expr} interpolation and global lookups.</summary>
    public required VirtualMachine Vm { get; init; }

    /// <summary>Cache for PATH-resolution of bare command names.</summary>
    public required PathExecutableCache PathCache { get; init; }

    /// <summary>All hard + soft Stash keywords (38 total). Bare identifiers in this set → Stash mode.</summary>
    public required IReadOnlySet<string> Keywords { get; init; }

    /// <summary>All 36 stdlib namespace names (fs, path, process, …). Bare identifiers in this set → Stash mode.</summary>
    public required IReadOnlySet<string> Namespaces { get; init; }

    /// <summary>
    /// Shell built-in names recognised by the classifier (cd, pwd, exit, quit).
    /// Phase 4: used only during classification. Runner does NOT desugar these yet.
    /// </summary>
    public required IReadOnlySet<string> ShellBuiltinNames { get; init; }

    // ── Well-known keyword set, built once ──────────────────────────────────

    /// <summary>
    /// Returns the canonical set of all 38 Stash keywords (32 hard + 6 soft)
    /// that should force Stash mode when they appear as the first token.
    /// </summary>
    internal static IReadOnlySet<string> BuildKeywordSet() =>
        new HashSet<string>(System.StringComparer.Ordinal)
        {
            // 32 hard keywords
            "let", "const", "fn", "struct", "enum", "interface",
            "if", "else", "for", "in", "while", "do",
            "return", "break", "continue",
            "true", "false", "null",
            "try", "throw", "catch", "finally",
            "import", "as", "switch", "case", "default",
            "is", "extend", "and", "or",
            // 6 soft keywords (contextual, but still force Stash mode as first token)
            "async", "await", "defer", "lock", "elevate", "retry",
        };

    internal static IReadOnlySet<string> BuildShellBuiltinSet() =>
        new HashSet<string>(System.StringComparer.Ordinal)
        {
            "cd", "pwd", "exit", "quit",
        };
}
