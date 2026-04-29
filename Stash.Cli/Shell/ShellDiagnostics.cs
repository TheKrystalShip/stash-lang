using System;
using System.Collections.Generic;
using System.IO;

namespace Stash.Cli.Shell;

/// <summary>
/// Diagnostic channel for REPL-only shell-mode diagnostics (SA0821, etc.).
/// These diagnostics are never routed through the analysis engine — they are
/// emitted directly to the REPL output during line classification.
/// </summary>
internal static class ShellDiagnostics
{
    private static readonly HashSet<string> _suppressedShadowWarnings = new(StringComparer.Ordinal);
    private static readonly object _lock = new();
    private static TextWriter _out = Console.Error;

    /// <summary>Override the output writer (for tests). Pass <c>null</c> to reset to <see cref="Console.Error"/>.</summary>
    public static void SetWriter(TextWriter? writer)
    {
        lock (_lock)
        {
            _out = writer ?? Console.Error;
        }
    }

    /// <summary>Reset per-session suppression state (for tests or REPL restart).</summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _suppressedShadowWarnings.Clear();
        }
    }

    /// <summary>
    /// Emit SA0821 for an identifier that shadows a PATH executable.
    /// Idempotent per-name per-session — only the first occurrence prints;
    /// subsequent calls for the same name are silent.
    /// </summary>
    public static void EmitShadowWarning(string identifier)
    {
        lock (_lock)
        {
            if (!_suppressedShadowWarnings.Add(identifier))
                return;

            _out.WriteLine($"stash: info SA0821: bare identifier '{identifier}' may shadow PATH executable in shell mode");
            _out.WriteLine($"  hint: pass \\{identifier} to force shell execution. This warning will not show again this session.");
        }
    }
}
