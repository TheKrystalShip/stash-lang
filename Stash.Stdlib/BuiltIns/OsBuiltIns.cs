namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
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

    // ── Named constants for Stash-visible string values ──────────────────────

    private const string EndiannessLittle = "little";
    private const string EndiannessBig    = "big";

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

    // ── Runtime architecture functions ────────────────────────────────────────

    /// <summary>
    /// Returns the operating-system architecture as a lowercase string sourced from
    /// <c>RuntimeInformation.OSArchitecture</c>.
    /// Known values at time of writing: <c>"x86"</c>, <c>"x64"</c>, <c>"arm"</c>,
    /// <c>"arm64"</c>, <c>"wasm"</c>, <c>"s390x"</c>, <c>"loongarch64"</c>,
    /// <c>"armv6"</c>, <c>"ppc64le"</c>, <c>"riscv64"</c>.
    /// The set is open and runtime-derived — new .NET targets may produce additional values.
    /// </summary>
    /// <returns>Lowercase OS architecture string.</returns>
    [StashFn]
    public static string Arch()
        => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    /// <summary>
    /// Returns the process architecture as a lowercase string sourced from
    /// <c>RuntimeInformation.ProcessArchitecture</c>.
    /// Known values at time of writing: <c>"x86"</c>, <c>"x64"</c>, <c>"arm"</c>,
    /// <c>"arm64"</c>, <c>"wasm"</c>, <c>"s390x"</c>, <c>"loongarch64"</c>,
    /// <c>"armv6"</c>, <c>"ppc64le"</c>, <c>"riscv64"</c>.
    /// The set is open and runtime-derived — new .NET targets may produce additional values.
    /// </summary>
    /// <returns>Lowercase process architecture string.</returns>
    [StashFn]
    public static string ProcessArch()
        => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    // ── Runtime metadata functions ────────────────────────────────────────────

    /// <summary>
    /// Returns a human-readable description of the current operating system,
    /// sourced from <c>RuntimeInformation.OSDescription</c>.
    /// Example: <c>"Linux 6.1.0-21-amd64 #1 SMP Debian 6.1.90-1 (2024-05-03)"</c>.
    /// </summary>
    /// <returns>OS description string.</returns>
    [StashFn]
    public static string Description()
        => RuntimeInformation.OSDescription;

    /// <summary>
    /// Returns a description of the .NET framework in use,
    /// sourced from <c>RuntimeInformation.FrameworkDescription</c>.
    /// Example: <c>".NET 8.0.6"</c>.
    /// </summary>
    /// <returns>Framework description string.</returns>
    [StashFn]
    public static string Framework()
        => RuntimeInformation.FrameworkDescription;

    /// <summary>
    /// Returns the operating system version string sourced from
    /// <c>Environment.OSVersion.VersionString</c>.
    /// Example: <c>"Unix 6.1.0.21"</c> or <c>"Microsoft Windows NT 10.0.19045.0"</c>.
    /// </summary>
    /// <returns>OS version string.</returns>
    [StashFn]
    public static string Version()
        => Environment.OSVersion.VersionString;

    /// <summary>
    /// Returns the byte order of the current host as a string.
    /// Returns <c>"little"</c> on little-endian hosts and <c>"big"</c> on big-endian hosts,
    /// based on <c>BitConverter.IsLittleEndian</c>.
    /// </summary>
    /// <returns><c>"little"</c> or <c>"big"</c>.</returns>
    [StashFn]
    public static string Endianness()
        => BitConverter.IsLittleEndian ? EndiannessLittle : EndiannessBig;

    // ── Aggregate snapshot ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a <c>PlatformInfo</c> struct snapshot containing all nine platform and runtime
    /// metadata fields evaluated at the moment of the call.
    /// Each field is identical to the value returned by the corresponding individual <c>os.*</c>
    /// function (e.g. <c>os.info().platform</c> equals <c>os.platform()</c>).
    /// </summary>
    /// <returns>A <c>PlatformInfo</c> struct with fields:
    /// <c>platform</c>, <c>name</c>, <c>isUnix</c>, <c>arch</c>, <c>processArch</c>,
    /// <c>description</c>, <c>framework</c>, <c>version</c>, <c>endianness</c>.</returns>
    [StashFn(Raw = true, ReturnType = nameof(PlatformInfo))]
    private static StashValue Info(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var platform = DetectPlatform();
        var fields = new Dictionary<string, StashValue>(9)
        {
            ["platform"]    = StashValue.FromObj(new StashEnumValue(nameof(Platform), platform.ToString())),
            ["name"]        = StashValue.FromObj(platform.ToString().ToLowerInvariant()),
            ["isUnix"]      = StashValue.FromBool(IsUnix()),
            ["arch"]        = StashValue.FromObj(Arch()),
            ["processArch"] = StashValue.FromObj(ProcessArch()),
            ["description"] = StashValue.FromObj(Description()),
            ["framework"]   = StashValue.FromObj(Framework()),
            ["version"]     = StashValue.FromObj(Version()),
            ["endianness"]  = StashValue.FromObj(Endianness()),
        };
        return StashValue.FromObj(new StashInstance(nameof(PlatformInfo), fields));
    }

    // ── Version-at-least helpers ──────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the host is macOS and the OS version is at least the specified
    /// major/minor/build. Returns <c>false</c> on non-macOS hosts without throwing.
    /// Delegates to <c>OperatingSystem.IsMacOSVersionAtLeast</c> on matching hosts.
    /// </summary>
    /// <param name="major">Major version number.</param>
    /// <param name="minor">(optional) Minor version number. Defaults to 0.</param>
    /// <param name="build">(optional) Build version number. Defaults to 0.</param>
    /// <returns><c>true</c> if running on macOS at or above the specified version, otherwise <c>false</c>.</returns>
    [StashFn(Name = "isMacOSVersionAtLeast")]
    public static bool IsMacOsVersionAtLeast(long major, long minor = 0L, long build = 0L)
        => OperatingSystem.IsMacOSVersionAtLeast((int)major, (int)minor, (int)build);

    /// <summary>
    /// Returns <c>true</c> if the host is Windows and the OS version is at least the specified
    /// major/minor/build/revision. Returns <c>false</c> on non-Windows hosts without throwing.
    /// Delegates to <c>OperatingSystem.IsWindowsVersionAtLeast</c> on matching hosts.
    /// </summary>
    /// <param name="major">Major version number.</param>
    /// <param name="minor">(optional) Minor version number. Defaults to 0.</param>
    /// <param name="build">(optional) Build version number. Defaults to 0.</param>
    /// <param name="revision">(optional) Revision number. Defaults to 0.</param>
    /// <returns><c>true</c> if running on Windows at or above the specified version, otherwise <c>false</c>.</returns>
    [StashFn]
    public static bool IsWindowsVersionAtLeast(long major, long minor = 0L, long build = 0L, long revision = 0L)
        => OperatingSystem.IsWindowsVersionAtLeast((int)major, (int)minor, (int)build, (int)revision);

    /// <summary>
    /// Returns <c>true</c> if the host is Linux and the kernel version is at least the specified
    /// major/minor. Returns <c>false</c> on non-Linux hosts without throwing.
    /// Uses <c>Environment.OSVersion.Version</c> (which reflects the kernel version from
    /// <c>uname -r</c>) for comparison on Linux hosts; this is the kernel version, not a
    /// distribution release number, because .NET exposes no <c>OperatingSystem.IsLinuxVersionAtLeast</c>
    /// equivalent.
    /// </summary>
    /// <param name="major">Major version number.</param>
    /// <param name="minor">(optional) Minor version number. Defaults to 0.</param>
    /// <returns><c>true</c> if running on Linux at or above the specified version, otherwise <c>false</c>.</returns>
    [StashFn]
    public static bool IsLinuxVersionAtLeast(long major, long minor = 0L)
    {
        if (!OperatingSystem.IsLinux()) return false;
        var v = Environment.OSVersion.Version;
        if (v.Major != (int)major) return v.Major > (int)major;
        return v.Minor >= (int)minor;
    }
}
