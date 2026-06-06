namespace Stash.Bytecode;

using System;
using System.Collections.Generic;
using System.IO;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// D1 — Unobserved-task exit report.
/// Scans a <see cref="SpawnedFutureRegistry"/> for faulted-but-never-awaited futures and
/// writes a warning block to a <see cref="TextWriter"/>.
///
/// <para>
/// Extracted from <c>Stash.Cli.Program</c> into <c>Stash.Bytecode</c> so that tests can
/// assert the report format without spawning a separate process. The CLI driver calls
/// <see cref="Report"/> via <c>Program.ReportUnobservedFaults</c>; tests call it directly.
/// </para>
/// </summary>
public static class UnobservedFaultReporter
{
    /// <summary>
    /// Scans the <paramref name="registry"/> for futures matching
    /// <c>IsFaulted &amp;&amp; !Observed &amp;&amp; !IsCancelled</c> and writes a warning
    /// block to <paramref name="errorOutput"/>. No-op when no unobserved faults exist or
    /// when <paramref name="embeddedMode"/> is <c>true</c> (embedded hosts control their
    /// own error output and must not receive surprise writes from D1).
    /// </summary>
    /// <param name="registry">The registry to scan.</param>
    /// <param name="errorOutput">Where to write the warning (typically stderr).</param>
    /// <param name="embeddedMode">
    /// When <c>true</c>, suppress all output and return 0 immediately.
    /// The default (<c>false</c>) is the CLI / normal execution path.
    /// </param>
    /// <returns>The number of unobserved faults reported (0 if none or suppressed).</returns>
    public static int Report(SpawnedFutureRegistry registry, TextWriter errorOutput, bool embeddedMode = false)
    {
        // EmbeddedMode hosts opt out of the report — they control their own error output
        // and must not receive surprise stderr writes from D1.
        if (embeddedMode) return 0;

        var faults = new List<StashFuture>(registry.UnobservedFaults());
        if (faults.Count == 0) return 0;

        errorOutput.WriteLine($"warning: {faults.Count} unobserved async error(s):");
        foreach (StashFuture f in faults)
        {
            try
            {
                f.GetResult(); // always throws for faulted futures
            }
            catch (RuntimeError re)
            {
                errorOutput.WriteLine($"  {re.ErrorType}: {re.Message}");
            }
            catch (Exception ex)
            {
                errorOutput.WriteLine($"  error: {ex.Message}");
            }
        }
        return faults.Count;
    }
}
