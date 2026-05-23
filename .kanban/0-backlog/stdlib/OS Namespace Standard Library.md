# OS Namespace Standard Library

Status: proposed

## Motivation

Stash should be portable across every platform .NET supports without becoming a POSIX shell. Scripts need a reliable way to branch on the host platform, architecture, and platform conventions while keeping Stash language semantics independent from POSIX shell compatibility.

The standard library should expose .NET's platform detection APIs directly through a small `os` namespace. This keeps the behavior aligned with the runtime Stash already depends on, avoids bespoke detection logic, and gives users a clear place to ask "what am I running on?"

## Goals

- Add an ungated `os` namespace for platform introspection.
- Use .NET runtime APIs as the source of truth.
- Keep the API deterministic, side-effect free, and available in all sandbox/capability profiles.
- Separate platform identity from environment-variable management.
- Keep Stash portable, not POSIX-compliant.

## Non-Goals

- Do not make Stash syntax or command execution POSIX shell-compatible.
- Do not emulate POSIX utilities or shell built-ins in this namespace.
- Do not expose environment variable mutation through `os`; that remains `env`.
- Do not expose hardware/resource metrics through `os`; those remain `sys`.
- Do not expose host identity (hostname, user) through `os`; those remain `env`.
- Do not expose stream/text conventions (`newLine`, `pathSeparator`) through `os`; those move to `io`.

## Supported Platforms

Stash will run wherever .NET runs. The `Platform` enum exposes every platform .NET's `OperatingSystem` class can identify, so scripts can branch portably. However, Stash is **only tested on Windows, Linux, and Wasm** at this stage of the project. The other platform members are exposed for forward compatibility and will work in theory but are not part of the supported-and-tested matrix.

## Namespace

Add `Stash.Stdlib/BuiltIns/OsBuiltIns.cs`:

```csharp
namespace Stash.Stdlib.BuiltIns;

using System;
using System.Runtime.InteropServices;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>Platform and operating-system introspection helpers.</summary>
[StashNamespace]
public static partial class OsBuiltIns
{
    /// <summary>
    /// Operating-system platform categories. Members mirror the discrete
    /// platforms .NET's <see cref="OperatingSystem"/> class can identify.
    /// Stash is officially tested on Windows, Linux, and Wasm; other members
    /// are exposed for forward compatibility and will work in theory.
    /// </summary>
    [StashEnum]
    public enum Platform
    {
        Windows, Linux, MacOS, FreeBSD, Android, IOS, TvOS, WatchOS, Browser, Wasi, Unknown
    }

    /// <summary>Snapshot of the current operating-system and runtime platform.</summary>
    [StashStruct]
    public sealed record PlatformInfo
    {
        [StashField(Type = "Platform")]
        public StashEnumValue Platform { get; init; } = default!;
        public string Name { get; init; } = "";
        public bool IsUnix { get; init; }
        public string Arch { get; init; } = "";
        public string ProcessArch { get; init; } = "";
        public string Description { get; init; } = "";
        public string Framework { get; init; } = "";
        public string Version { get; init; } = "";
        public string Endianness { get; init; } = "";
    }
}
```

### Why no capability gate

`os` reports immutable, process-scoped facts about the runtime and host platform. It never reads files, environment variables, or the network, and it never mutates host state. Reading platform identity is a precondition for writing portable scripts, so the namespace is available in every sandbox profile — including the strictest. It sits in the same trust tier as `math`, `str`, and `conv`.

## Public API

### Platform Identity

| Function | Return | .NET source | Description |
| --- | --- | --- | --- |
| `os.platform()` | `Platform` | `OperatingSystem.Is{Platform}()` calls | Returns the matching `Platform` member, or `Platform.Unknown` if no .NET probe matches. |
| `os.name()` | `string` | `os.platform()` | Returns a stable lowercase name (`"windows"`, `"linux"`, `"macos"`, `"freebsd"`, `"android"`, `"ios"`, `"tvos"`, `"watchos"`, `"browser"`, `"wasi"`, `"unknown"`) for command arguments, file names, and display text. |
| `os.isWindows()` | `bool` | `OperatingSystem.IsWindows()` | True on Windows. |
| `os.isLinux()` | `bool` | `OperatingSystem.IsLinux()` | True on Linux. |
| `os.isMacOS()` | `bool` | `OperatingSystem.IsMacOS()` | True on macOS. |
| `os.isFreeBSD()` | `bool` | `OperatingSystem.IsFreeBSD()` | True on FreeBSD. |
| `os.isAndroid()` | `bool` | `OperatingSystem.IsAndroid()` | True on Android. |
| `os.isIOS()` | `bool` | `OperatingSystem.IsIOS()` | True on iOS. |
| `os.isBrowser()` | `bool` | `OperatingSystem.IsBrowser()` | True under a browser WebAssembly host. |
| `os.isUnix()` | `bool` | aggregate | A Stash portability convenience — true for the Unix-like targets Stash exposes (`Linux`, `MacOS`, `FreeBSD`, `Android`, `iOS`, `tvOS`, `watchOS`). This is **not** a POSIX compliance claim, and the function's `<summary>` must say so. |

`os.platform()` is the preferred API for Stash control flow. `os.name()` exists for interop with external commands, config files, logs, and places where a stable string is more useful than a Stash enum.

The `Platform` member spelling follows .NET enum naming verbatim (`MacOS`, `IOS`, `TvOS`, `WatchOS`) so the C# source stays readable next to `OperatingSystem.IsMacOS()` / `IsIOS()`. The `os.name()` strings are the lowercase forms developers expect (`"macos"`, `"ios"`, etc.).

The `os.name()` and `os.isXxx()` string and boolean values are part of the API contract. They will not change with platform localization or runtime spelling.

### Runtime Architecture

| Function | Return | .NET source | Description |
| --- | --- | --- | --- |
| `os.arch()` | `string` | `RuntimeInformation.OSArchitecture` | Returns the OS architecture as the runtime's name, lowercased — `"x64"`, `"arm64"`, `"loongarch64"`, `"riscv64"`, etc. |
| `os.processArch()` | `string` | `RuntimeInformation.ProcessArchitecture` | Returns the architecture of the running Stash process as the runtime's name, lowercased. |
| `os.description()` | `string` | `RuntimeInformation.OSDescription` | Returns the runtime's OS description string. |
| `os.framework()` | `string` | `RuntimeInformation.FrameworkDescription` | Returns the current .NET runtime description. |
| `os.version()` | `string` | `Environment.OSVersion.VersionString` | Returns the OS version string (e.g. `"Microsoft Windows NT 10.0.22631.0"`). |
| `os.endianness()` | `string` | `BitConverter.IsLittleEndian` | Returns `"little"` or `"big"`. Useful for binary protocols. |
| `os.isMacOSVersionAtLeast(major, minor?, build?)` | `bool` | `OperatingSystem.IsMacOSVersionAtLeast` | Forwards to the .NET helper. Returns `false` on non-macOS hosts. |
| `os.isWindowsVersionAtLeast(major, minor?, build?, revision?)` | `bool` | `OperatingSystem.IsWindowsVersionAtLeast` | Forwards to the .NET helper. Returns `false` on non-Windows hosts. |
| `os.isLinuxVersionAtLeast(major, minor?)` | `bool` | (build/probe — falsy fallback) | Returns `false` on non-Linux hosts and on Linux where the kernel version cannot be parsed; otherwise compares to `Environment.OSVersion`. |

#### Architecture forward-compatibility rationale

`arch()` and `processArch()` return raw lowercased runtime strings — **not** a Stash enum — by deliberate choice. Modeling architecture as an enum would force every new .NET architecture (e.g. RiscV64 added in .NET 9, future additions) to be backported into Stash before Stash could report it correctly; until that backport happened, scripts would see `Architecture.Unknown` while the runtime knew exactly what it was. The cost of that trap is greater than the discoverability cost of a string.

To recover some discoverability, the function `<summary>` enumerates the values known at the time of writing (`x86`, `x64`, `arm`, `arm64`, `wasm`, `s390x`, `loongarch64`, `armv6`, `ppc64le`, `riscv64`) and explicitly tells the reader that the set is open and runtime-derived.

### Platform Snapshot

| Function | Return | .NET source | Description |
| --- | --- | --- | --- |
| `os.info()` | `PlatformInfo` | all APIs above | Returns a typed snapshot of the current platform and runtime. |

`PlatformInfo` fields:

| Field | Type | Description |
| --- | --- | --- |
| `platform` | `Platform` | Current OS platform enum. |
| `name` | `string` | Stable lowercase platform name. |
| `isUnix` | `bool` | True for Unix-like platforms (see `os.isUnix()`). |
| `arch` | `string` | Lowercase OS architecture name. |
| `processArch` | `string` | Lowercase process architecture name. |
| `description` | `string` | Runtime OS description. |
| `framework` | `string` | .NET runtime description. |
| `version` | `string` | OS version string. |
| `endianness` | `string` | `"little"` or `"big"`. |

**Snapshot semantics:** every call to `os.info()` evaluates each field afresh — there is no memoization. Callers must not assume reference equality across calls. Although every field is process-immutable in practice, the spec promises only "evaluated once per call".

## Moved to `io`

`Path.PathSeparator` and `Environment.NewLine` are per-stream / per-text conventions rather than platform-identity facts, so they live in `io`, which already owns text I/O:

| Function | Return | .NET source | Description |
| --- | --- | --- | --- |
| `io.pathSeparator()` | `string` | `Path.PathSeparator` | Separator used between paths in PATH-like variables (`";"` on Windows, `":"` on Unix-like). |
| `io.newLine()` | `string` | `Environment.NewLine` | Platform newline sequence. |

These are not duplicated in `os` or in `PlatformInfo`. Implementing this spec includes adding both functions to `IoBuiltIns`, with the matching tests and docs.

Directory separator helpers remain in the `path` namespace, which already owns path manipulation.

## Existing API Cleanup

`env.os()` and `env.arch()` currently provide platform identity ([Stash.Stdlib/BuiltIns/EnvBuiltIns.cs:133-148](Stash.Stdlib/BuiltIns/EnvBuiltIns.cs#L133-L148)). Because Stash is pre-1.0, do not preserve duplicate compatibility aliases.

When implementing this spec:

- Move OS identity helpers to `os.platform()`, `os.name()`, `os.arch()`.
- Delete `env.os()` and `env.arch()` from `EnvBuiltIns.cs`.
- Update all callers found in tree:
  - `examples/system_info.stash`
  - `examples/namespaces.stash`
  - `Stash.Tests/Interpreting/EnvBuiltInsTests.cs` (remove the os/arch test cases or relocate to `OsBuiltInsTests`)
  - `Stash.Tests/Interpreting/InterpreterTests.cs`
  - `.github/instructions/stdlib.instructions.md`
  - `docs/Stash — Standard Library Reference.md` (regenerated; verify after run)
- Add a `CHANGELOG.md` entry under the breaking-changes section.
- Keep `env` focused on environment variables and user/session environment data such as `env.cwd()`, `env.home()`, `env.hostname()`, and `env.user()`.

## Semantics

- All functions are pure observations of runtime/platform state.
- No function reads or writes environment variables.
- No function shells out to external commands.
- No function parses `/etc/os-release`, `uname`, registry keys, or platform-specific files.
- Unknown platforms return `Platform.Unknown` from `os.platform()` and `"unknown"` from `os.name()` rather than throwing.
- Architecture strings are runtime-derived and not normalized beyond `ToLowerInvariant()`.
- `os.isUnix()` is a Stash portability convenience, not a POSIX compliance claim.
- Version-at-least helpers return `false` on non-matching hosts (e.g. `os.isMacOSVersionAtLeast(13)` is `false` on Linux), never throw.
- `os.info()` evaluates fields once per call; no memoization.

## Implementation Notes

Follow the existing `[StashEnum]` + `[StashStruct]` + `Raw = true` + `StashEnumValue` pattern established in [Stash.Stdlib/BuiltIns/FsBuiltIns.cs:150-172](Stash.Stdlib/BuiltIns/FsBuiltIns.cs#L150-L172) (e.g. `WatchEventType`, `FileInfo`) and `ProcessBuiltIns.cs` (`ExecMode`, `ProcessResult`). `os.platform()` and `os.info()` are `Raw = true` and construct `StashEnumValue` / `StashStructInstance` values explicitly. Use `nameof()` and named `private const` strings — no inline string literals for type or member names (per project rule against magic strings).

Sketch:

```csharp
private const string PlatformTypeName = nameof(Platform);
private const string WindowsName = "windows";
private const string LinuxName = "linux";
private const string MacOSName = "macos";
private const string FreeBSDName = "freebsd";
// … one const per Platform member …
private const string UnknownName = "unknown";
private const string EndiannessLittle = "little";
private const string EndiannessBig = "big";

private static StashEnumValue CurrentPlatformValue()
{
    if (OperatingSystem.IsWindows())  return new StashEnumValue(PlatformTypeName, nameof(Platform.Windows));
    if (OperatingSystem.IsLinux())    return new StashEnumValue(PlatformTypeName, nameof(Platform.Linux));
    if (OperatingSystem.IsMacOS())    return new StashEnumValue(PlatformTypeName, nameof(Platform.MacOS));
    if (OperatingSystem.IsFreeBSD())  return new StashEnumValue(PlatformTypeName, nameof(Platform.FreeBSD));
    if (OperatingSystem.IsAndroid())  return new StashEnumValue(PlatformTypeName, nameof(Platform.Android));
    if (OperatingSystem.IsIOS())      return new StashEnumValue(PlatformTypeName, nameof(Platform.IOS));
    if (OperatingSystem.IsTvOS())     return new StashEnumValue(PlatformTypeName, nameof(Platform.TvOS));
    if (OperatingSystem.IsWatchOS())  return new StashEnumValue(PlatformTypeName, nameof(Platform.WatchOS));
    if (OperatingSystem.IsBrowser())  return new StashEnumValue(PlatformTypeName, nameof(Platform.Browser));
    if (OperatingSystem.IsWasi())     return new StashEnumValue(PlatformTypeName, nameof(Platform.Wasi));
    return new StashEnumValue(PlatformTypeName, nameof(Platform.Unknown));
}

private static string PlatformName(StashEnumValue platform) => platform.MemberName switch
{
    nameof(Platform.Windows)  => WindowsName,
    nameof(Platform.Linux)    => LinuxName,
    nameof(Platform.MacOS)    => MacOSName,
    nameof(Platform.FreeBSD)  => FreeBSDName,
    // … one arm per member …
    _                         => UnknownName
};

public static string Arch()         => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
public static string ProcessArch()  => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
public static string Description()  => RuntimeInformation.OSDescription;
public static string Framework()    => RuntimeInformation.FrameworkDescription;
public static string Version()      => Environment.OSVersion.VersionString;
public static string Endianness()   => BitConverter.IsLittleEndian ? EndiannessLittle : EndiannessBig;
```

## Tests

Add `Stash.Tests/Stdlib/OsBuiltInsTests.cs`.

Required coverage:

- `Platform` enum metadata exposes every member listed in this spec.
- `PlatformInfo` struct metadata exposes every field listed in this spec.
- `os.platform()` returns the expected `Platform` enum member for the test host.
- `os.name()` returns the documented string corresponding to `os.platform()`.
- **Precondition-guarded**: when the test host is one of `Windows`/`Linux`/`MacOS`, assert exactly one of `os.isWindows()`/`os.isLinux()`/`os.isMacOS()` is true. Skip the assertion otherwise.
- **Precondition-guarded**: `os.isUnix()` equals "host is one of the Unix-like members" — assert on hosts where this is deterministic; skip on `Unknown`.
- `os.arch()` and `os.processArch()` return non-empty lowercase strings that round-trip through `RuntimeInformation`.
- `os.description()`, `os.framework()`, `os.version()` return non-empty strings.
- `os.endianness()` is `"little"` or `"big"` and matches `BitConverter.IsLittleEndian`.
- `os.isMacOSVersionAtLeast`, `os.isWindowsVersionAtLeast`, `os.isLinuxVersionAtLeast` return `false` on the wrong host without throwing.
- `os.info()` returns a `PlatformInfo` whose fields match the individual helper functions, and two successive calls produce equal values (snapshot semantics, no memoization).
- `env.os()` and `env.arch()` are no longer present in stdlib metadata.
- `os` appears in `StdlibRegistry.NamespaceNames`.
- `io.pathSeparator()` matches `Path.PathSeparator.ToString()` and `io.newLine()` matches `Environment.NewLine`.

Avoid platform-specific assertions that would only pass on one CI operating system. Where the host platform varies, branch using the same .NET platform APIs and assert that Stash exposes the same value.

## Documentation

Do not hand-edit `docs/Stash — Standard Library Reference.md`. After implementation, regenerate all generated docs:

```bash
dotnet run --project Stash.Docs/
```

Also update authored docs and the language specification where they mention `env.os()` or `env.arch()` to point at `os.name()` and `os.arch()`. Add a section to the spec documenting the new `os` namespace if a structural reference is appropriate.

## Example

```stash
if os.platform() == Platform.Windows {
    process.run("where", ["git"])
} else {
    process.run("which", ["git"])
}

let info = os.info();

let cacheDir = info.platform == Platform.MacOS
    ? env.home() + "/Library/Caches/stash"
    : env.home() + "/.cache/stash";

if os.isMacOSVersionAtLeast(13) {
    io.println("running on Ventura or newer")
}

if os.arch() == "arm64" {
    io.println("native arm64 build")
}
```

This keeps platform branching explicit without importing POSIX shell behavior into the language.
