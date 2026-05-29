namespace Stash.Stdlib.BuiltIns;

using System;
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

    // ── Private detection helper ──────────────────────────────────────────────

    /// <summary>
    /// Detects the current host platform from .NET runtime APIs.
    /// Returns <see cref="Platform.Unknown"/> on unrecognised hosts — never throws.
    /// </summary>
    private static Platform DetectPlatform()
    {
        if (OperatingSystem.IsWindows()) return Platform.Windows;
        if (OperatingSystem.IsMacOS())   return Platform.MacOS;
        if (OperatingSystem.IsAndroid()) return Platform.Android;
        if (OperatingSystem.IsIOS())     return Platform.IOS;
        if (OperatingSystem.IsTvOS())    return Platform.TvOS;
        if (OperatingSystem.IsWatchOS()) return Platform.WatchOS;
        if (OperatingSystem.IsFreeBSD()) return Platform.FreeBSD;
        if (OperatingSystem.IsBrowser()) return Platform.Browser;
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWasi())    return Platform.Wasi;
#endif
        if (OperatingSystem.IsLinux())   return Platform.Linux;
        return Platform.Unknown;
    }

    // ── Platform identity functions ───────────────────────────────────────────

    /// <summary>
    /// Returns the host platform as a <c>Platform</c> enum value.
    /// Sourced from <c>OperatingSystem.IsXxx()</c>; returns <c>Platform.Unknown</c> on
    /// unrecognised hosts — never throws.
    /// </summary>
    /// <returns>A <c>Platform</c> enum value identifying the host operating system.</returns>
    [StashFn(Raw = true, Name = "platform", ReturnType = nameof(Platform))]
    private static StashValue GetPlatform(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => StashValue.FromObj(new StashEnumValue(nameof(Platform), DetectPlatform().ToString()));

    /// <summary>
    /// Returns the host platform as a stable lowercase string.
    /// Possible values: <c>"windows"</c>, <c>"linux"</c>, <c>"macos"</c>, <c>"freebsd"</c>,
    /// <c>"android"</c>, <c>"ios"</c>, <c>"tvos"</c>, <c>"watchos"</c>, <c>"browser"</c>,
    /// <c>"wasi"</c>, or <c>"unknown"</c>. These strings are part of the API contract.
    /// </summary>
    /// <returns>Lowercase platform name string.</returns>
    [StashFn]
    public static string Name()
        => DetectPlatform().ToString().ToLowerInvariant();

    /// <summary>
    /// Returns <c>true</c> if the host operating system is Windows.
    /// Delegates to <c>OperatingSystem.IsWindows()</c>.
    /// </summary>
    /// <returns><c>true</c> on Windows, otherwise <c>false</c>.</returns>
    [StashFn]
    public static bool IsWindows() => OperatingSystem.IsWindows();

    /// <summary>
    /// Returns <c>true</c> if the host operating system is Linux.
    /// Delegates to <c>OperatingSystem.IsLinux()</c>.
    /// </summary>
    /// <returns><c>true</c> on Linux, otherwise <c>false</c>.</returns>
    [StashFn]
    public static bool IsLinux() => OperatingSystem.IsLinux();

    /// <summary>
    /// Returns <c>true</c> if the host operating system is macOS.
    /// Delegates to <c>OperatingSystem.IsMacOS()</c>.
    /// </summary>
    /// <returns><c>true</c> on macOS, otherwise <c>false</c>.</returns>
    [StashFn(Name = "isMacOS")]
    public static bool IsMacOs() => OperatingSystem.IsMacOS();

    /// <summary>
    /// Returns <c>true</c> if the host operating system is FreeBSD.
    /// Delegates to <c>OperatingSystem.IsFreeBSD()</c>.
    /// </summary>
    /// <returns><c>true</c> on FreeBSD, otherwise <c>false</c>.</returns>
    [StashFn(Name = "isFreeBSD")]
    public static bool IsFreeBsd() => OperatingSystem.IsFreeBSD();

    /// <summary>
    /// Returns <c>true</c> if the host operating system is Android.
    /// Delegates to <c>OperatingSystem.IsAndroid()</c>.
    /// </summary>
    /// <returns><c>true</c> on Android, otherwise <c>false</c>.</returns>
    [StashFn]
    public static bool IsAndroid() => OperatingSystem.IsAndroid();

    /// <summary>
    /// Returns <c>true</c> if the host operating system is iOS.
    /// Delegates to <c>OperatingSystem.IsIOS()</c>.
    /// </summary>
    /// <returns><c>true</c> on iOS, otherwise <c>false</c>.</returns>
    [StashFn(Name = "isIOS")]
    public static bool IsIos() => OperatingSystem.IsIOS();

    /// <summary>
    /// Returns <c>true</c> if the host is running in a browser (WebAssembly).
    /// Delegates to <c>OperatingSystem.IsBrowser()</c>.
    /// </summary>
    /// <returns><c>true</c> in a browser/Wasm environment, otherwise <c>false</c>.</returns>
    [StashFn]
    public static bool IsBrowser() => OperatingSystem.IsBrowser();

    /// <summary>
    /// Returns <c>true</c> if the host is a Unix-like operating system: Linux, macOS, FreeBSD,
    /// Android, iOS, tvOS, or watchOS. This is a portability convenience predicate —
    /// it is <strong>not</strong> a POSIX compliance claim. Stash makes no promise about
    /// POSIX API availability on these platforms.
    /// </summary>
    /// <returns><c>true</c> on Unix-like hosts, otherwise <c>false</c>.</returns>
    [StashFn]
    public static bool IsUnix()
        => OperatingSystem.IsLinux()
        || OperatingSystem.IsMacOS()
        || OperatingSystem.IsFreeBSD()
        || OperatingSystem.IsAndroid()
        || OperatingSystem.IsIOS()
        || OperatingSystem.IsTvOS()
        || OperatingSystem.IsWatchOS();
}
