namespace Stash.Hosting;

using System.IO;
using Stash.Runtime;

/// <summary>
/// Configuration options for a <see cref="StashHost"/> instance.
/// </summary>
public sealed class StashHostOptions
{
    /// <summary>
    /// The capabilities granted to scripts running in this host.
    /// Defaults to <see cref="StashCapabilities.All"/>.
    /// </summary>
    public StashCapabilities Capabilities { get; init; } = StashCapabilities.All;

    /// <summary>
    /// The maximum number of VM steps a script may execute before
    /// <see cref="Stash.Runtime.StepLimitExceededException"/> is thrown.
    /// A value of 0 (default) means no limit.
    /// </summary>
    public long StepLimit { get; init; }

    /// <summary>
    /// The text writer used for script output (<c>io.println</c>, etc.).
    /// <c>null</c> discards all output.
    /// </summary>
    public TextWriter? Output { get; init; }

    /// <summary>
    /// The text writer used for script error output.
    /// <c>null</c> discards all error output.
    /// </summary>
    public TextWriter? ErrorOutput { get; init; }
}
