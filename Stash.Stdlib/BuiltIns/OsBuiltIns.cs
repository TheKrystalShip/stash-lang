namespace Stash.Stdlib.BuiltIns;

using System.Runtime.InteropServices;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>os</c> namespace built-in functions for platform and operating-system introspection.
/// </summary>
/// <remarks>
/// <para>
/// Provides a pure, side-effect-free surface for querying runtime platform identity and metadata.
/// All information is sourced from .NET runtime APIs (<c>OperatingSystem</c>,
/// <c>RuntimeInformation</c>, <c>Environment.OSVersion</c>, and <c>BitConverter</c>).
/// No environment variable reads, no shell-outs, no parsing of <c>/etc/os-release</c> or the registry.
/// </para>
/// <para>
/// This namespace carries no capability gate — it reports immutable, process-scoped facts and
/// is available in every sandbox profile (same trust tier as <c>math</c>, <c>str</c>, <c>conv</c>).
/// </para>
/// </remarks>
[StashNamespace]
public static partial class OsBuiltIns
{
    // ── Stash enum declarations ───────────────────────────────────────────────

    /// <summary>
    /// The host operating system platform, as reported by the .NET runtime.
    /// Member spelling follows .NET enum naming verbatim (<c>OperatingSystem.IsXxx()</c> parity).
    /// Only <c>Windows</c>, <c>Linux</c>, and <c>Browser</c> (Wasm) are officially tested;
    /// all other members are present for forward-compatibility with every platform .NET supports.
    /// </summary>
    [StashEnum]
    public enum Platform
    {
        Windows,
        Linux,
        MacOS,
        FreeBSD,
        Android,
        IOS,
        TvOS,
        WatchOS,
        Browser,
        Wasi,
        Unknown,
    }

    // ── Stash struct declarations ─────────────────────────────────────────────

    /// <summary>
    /// A snapshot of platform identity and runtime metadata returned by <c>os.info()</c>.
    /// Fields mirror the individual <c>os.*</c> helpers evaluated once per call;
    /// no memoization and no reference-equality guarantee across calls.
    /// </summary>
    [StashStruct]
    public sealed record PlatformInfo(
        Platform Platform,
        string Name,
        bool IsUnix,
        string Arch,
        string ProcessArch,
        string Description,
        string Framework,
        string Version,
        string Endianness);
}
