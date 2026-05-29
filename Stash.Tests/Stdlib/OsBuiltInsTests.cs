namespace Stash.Tests.Stdlib;

using System;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Stdlib.Models;
using Stash.Tests.Interpreting;

/// <summary>
/// Dedicated test suite for the <c>os</c> namespace introduced in the os-namespace feature.
/// Tests cover: enum and struct metadata, platform/name match, precondition-guarded
/// isWindows/isLinux/isMacOS predicates, isUnix, arch/processArch round-trip,
/// description/framework/version non-empty, endianness match, version-at-least helpers,
/// info() field equality and two-call equality, env.os/env.arch absence, and os in NamespaceNames.
/// </summary>
/// <remarks>
/// Every platform-specific assertion is precondition-guarded using the same .NET runtime APIs
/// as the implementation, so the suite passes on any developer host.
/// </remarks>
public class OsBuiltInsTests : StashTestBase
{
    // =========================================================================
    // Platform enum metadata
    // =========================================================================

    [Fact]
    public void Platform_Enum_HasCorrectName()
    {
        var platformEnum = StdlibRegistry.Enums.FirstOrDefault(e => e.Name == nameof(Stash.Stdlib.BuiltIns.OsBuiltIns.Platform));
        Assert.NotNull(platformEnum);
    }

    [Fact]
    public void Platform_Enum_HasAllExpectedMembers()
    {
        var platformEnum = StdlibRegistry.Enums.FirstOrDefault(e => e.Name == nameof(Stash.Stdlib.BuiltIns.OsBuiltIns.Platform));
        Assert.NotNull(platformEnum);

        string[] expectedMembers = ["Windows", "Linux", "MacOS", "FreeBSD", "Android", "IOS", "TvOS", "WatchOS", "Browser", "Wasi", "Unknown"];
        foreach (string member in expectedMembers)
        {
            Assert.Contains(member, platformEnum!.Members);
        }
    }

    [Fact]
    public void Platform_Enum_BelongsToOsNamespace()
    {
        var platformEnum = StdlibRegistry.Enums.FirstOrDefault(e => e.Name == nameof(Stash.Stdlib.BuiltIns.OsBuiltIns.Platform));
        Assert.NotNull(platformEnum);
        Assert.Equal("os", platformEnum!.Namespace);
    }

    // =========================================================================
    // PlatformInfo struct metadata
    // =========================================================================

    [Fact]
    public void PlatformInfo_Struct_HasCorrectName()
    {
        var info = StdlibRegistry.Structs.FirstOrDefault(s => s.Name == nameof(Stash.Stdlib.BuiltIns.OsBuiltIns.PlatformInfo));
        Assert.NotNull(info);
    }

    [Fact]
    public void PlatformInfo_Struct_HasAllExpectedFields()
    {
        var info = StdlibRegistry.Structs.FirstOrDefault(s => s.Name == nameof(Stash.Stdlib.BuiltIns.OsBuiltIns.PlatformInfo));
        Assert.NotNull(info);

        string[] expectedFields = ["platform", "name", "isUnix", "arch", "processArch", "description", "framework", "version", "endianness"];
        var actualFieldNames = info!.Fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string field in expectedFields)
        {
            Assert.True(actualFieldNames.Contains(field), $"PlatformInfo struct is missing field '{field}'");
        }
    }

    // =========================================================================
    // os in NamespaceNames
    // =========================================================================

    [Fact]
    public void Os_AppearsInNamespaceNames()
    {
        Assert.Contains("os", StdlibRegistry.NamespaceNames);
    }

    // =========================================================================
    // os.platform() and os.name() match the host
    // =========================================================================

    [Fact]
    public void Platform_ReturnsEnumValueMatchingDotNetRuntime()
    {
        // Compare the Stash platform name string against the expected value derived
        // from the same .NET APIs used by the implementation.
        string expectedName = GetExpectedPlatformName();
        var result = Run("let result = os.name();");
        Assert.Equal(expectedName, result);
    }

    [Fact]
    public void Platform_IsStashEnumValueWithCorrectTypeName()
    {
        // os.platform() returns a StashEnumValue whose TypeName is "Platform".
        var result = Run("let result = os.platform();");
        var enumVal = Assert.IsType<StashEnumValue>(result);
        Assert.Equal(nameof(Stash.Stdlib.BuiltIns.OsBuiltIns.Platform), enumVal.TypeName);
    }

    [Fact]
    public void Platform_TypeofIsEnum()
    {
        var result = Run("let result = typeof(os.platform());");
        Assert.Equal("enum", result);
    }

    [Fact]
    public void Platform_MemberNameMatchesName()
    {
        // The enum member name lowercased must equal os.name().
        var platformResult = Run("let result = os.platform();");
        var enumVal = Assert.IsType<StashEnumValue>(platformResult);
        string expectedName = enumVal.MemberName.ToLowerInvariant();

        var nameResult = Run("let result = os.name();");
        Assert.Equal(expectedName, nameResult);
    }

    // =========================================================================
    // Precondition-guarded: isWindows / isLinux / isMacOS — exactly one true
    // =========================================================================

    [Fact]
    public void IsWindows_MatchesDotNetApi()
    {
        var result = Run("let result = os.isWindows();");
        Assert.Equal(OperatingSystem.IsWindows(), result);
    }

    [Fact]
    public void IsLinux_MatchesDotNetApi()
    {
        var result = Run("let result = os.isLinux();");
        Assert.Equal(OperatingSystem.IsLinux(), result);
    }

    [Fact]
    public void IsMacOs_MatchesDotNetApi()
    {
        var result = Run("let result = os.isMacOS();");
        Assert.Equal(OperatingSystem.IsMacOS(), result);
    }

    [Fact]
    public void ExactlyOneOf_IsWindows_IsLinux_IsMacOs_IsTrue_OnKnownHosts()
    {
        // Precondition-guard: only assert the "exactly one" constraint when the host
        // is one of the three known platforms — skip on FreeBSD, Android, Wasm, etc.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        // Stash requires parenthesized conditions in if statements.
        var result = Run("""
            let trueCount = 0;
            if (os.isWindows()) { trueCount = trueCount + 1; }
            if (os.isLinux())   { trueCount = trueCount + 1; }
            if (os.isMacOS())   { trueCount = trueCount + 1; }
            let result = trueCount;
            """);
        Assert.Equal(1L, result);
    }

    // =========================================================================
    // os.isUnix()
    // =========================================================================

    [Fact]
    public void IsUnix_MatchesDotNetApi()
    {
        bool expectedUnix = OperatingSystem.IsLinux()
            || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD()
            || OperatingSystem.IsAndroid()
            || OperatingSystem.IsIOS()
            || OperatingSystem.IsTvOS()
            || OperatingSystem.IsWatchOS();

        var result = Run("let result = os.isUnix();");
        Assert.Equal(expectedUnix, result);
    }

    // =========================================================================
    // os.arch() and os.processArch() round-trip
    // =========================================================================

    [Fact]
    public void Arch_ReturnsNonEmptyLowercaseString()
    {
        var result = Run("let result = os.arch();");
        Assert.IsType<string>(result);
        var s = (string)result!;
        Assert.NotEmpty(s);
        Assert.Equal(s.ToLowerInvariant(), s);
    }

    [Fact]
    public void Arch_MatchesDotNetOsArchitecture()
    {
        string expected = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var result = Run("let result = os.arch();");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProcessArch_ReturnsNonEmptyLowercaseString()
    {
        var result = Run("let result = os.processArch();");
        Assert.IsType<string>(result);
        var s = (string)result!;
        Assert.NotEmpty(s);
        Assert.Equal(s.ToLowerInvariant(), s);
    }

    [Fact]
    public void ProcessArch_MatchesDotNetProcessArchitecture()
    {
        string expected = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var result = Run("let result = os.processArch();");
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // os.description() / os.framework() / os.version() — non-empty strings
    // =========================================================================

    [Fact]
    public void Description_ReturnsNonEmptyString()
    {
        var result = Run("let result = os.description();");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void Framework_ReturnsNonEmptyString()
    {
        var result = Run("let result = os.framework();");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void Version_ReturnsNonEmptyString()
    {
        var result = Run("let result = os.version();");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    // =========================================================================
    // os.endianness() — matches BitConverter
    // =========================================================================

    [Fact]
    public void Endianness_IsLittleOrBig()
    {
        var result = Run("let result = os.endianness();");
        Assert.IsType<string>(result);
        var s = (string)result!;
        Assert.True(s == "little" || s == "big", $"Expected 'little' or 'big', got '{s}'");
    }

    [Fact]
    public void Endianness_MatchesBitConverterIsLittleEndian()
    {
        string expected = BitConverter.IsLittleEndian ? "little" : "big";
        var result = Run("let result = os.endianness();");
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // Version-at-least helpers — false on wrong host without throwing
    // =========================================================================

    [Fact]
    public void IsMacOsVersionAtLeast_ReturnsFalseOnNonMacOs()
    {
        if (OperatingSystem.IsMacOS()) return;
        var result = Run("let result = os.isMacOSVersionAtLeast(10);");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IsWindowsVersionAtLeast_ReturnsFalseOnNonWindows()
    {
        if (OperatingSystem.IsWindows()) return;
        var result = Run("let result = os.isWindowsVersionAtLeast(10);");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IsLinuxVersionAtLeast_ReturnsFalseOnNonLinux()
    {
        if (OperatingSystem.IsLinux()) return;
        var result = Run("let result = os.isLinuxVersionAtLeast(1);");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IsMacOsVersionAtLeast_DoesNotThrowOnAnyHost()
    {
        var ex = Record.Exception(() => Run("let result = os.isMacOSVersionAtLeast(99, 99, 99);"));
        Assert.Null(ex);
    }

    [Fact]
    public void IsWindowsVersionAtLeast_DoesNotThrowOnAnyHost()
    {
        var ex = Record.Exception(() => Run("let result = os.isWindowsVersionAtLeast(99, 0, 0, 0);"));
        Assert.Null(ex);
    }

    [Fact]
    public void IsLinuxVersionAtLeast_DoesNotThrowOnAnyHost()
    {
        var ex = Record.Exception(() => Run("let result = os.isLinuxVersionAtLeast(999, 999);"));
        Assert.Null(ex);
    }

    // =========================================================================
    // os.info() — field equality and two-call equality
    // =========================================================================

    [Fact]
    public void Info_ReturnsStashInstanceWithCorrectTypeName()
    {
        // os.info() returns a StashInstance whose TypeName is "PlatformInfo".
        var result = Run("let result = os.info();");
        var inst = Assert.IsType<StashInstance>(result);
        Assert.Equal(nameof(Stash.Stdlib.BuiltIns.OsBuiltIns.PlatformInfo), inst.TypeName);
    }

    [Fact]
    public void Info_TypeofIsStruct()
    {
        var result = Run("let result = typeof(os.info());");
        Assert.Equal("struct", result);
    }

    [Fact]
    public void Info_PlatformField_MatchesPlatform()
    {
        // Info returns a StashInstance (reference equality only in Stash ==).
        // Assert field values by extracting them individually.
        var result = Run("let info = os.info(); let result = info.name;");
        var expected = Run("let result = os.name();");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Info_IsUnixField_MatchesIsUnix()
    {
        var result = Run("let info = os.info(); let result = info.isUnix;");
        var expected = Run("let result = os.isUnix();");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Info_ArchField_MatchesArch()
    {
        var result = Run("let info = os.info(); let result = info.arch;");
        var expected = Run("let result = os.arch();");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Info_ProcessArchField_MatchesProcessArch()
    {
        var result = Run("let info = os.info(); let result = info.processArch;");
        var expected = Run("let result = os.processArch();");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Info_DescriptionField_MatchesDescription()
    {
        var result = Run("let info = os.info(); let result = info.description;");
        var expected = Run("let result = os.description();");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Info_FrameworkField_MatchesFramework()
    {
        var result = Run("let info = os.info(); let result = info.framework;");
        var expected = Run("let result = os.framework();");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Info_VersionField_MatchesVersion()
    {
        var result = Run("let info = os.info(); let result = info.version;");
        var expected = Run("let result = os.version();");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Info_EndiannessField_MatchesEndianness()
    {
        var result = Run("let info = os.info(); let result = info.endianness;");
        var expected = Run("let result = os.endianness();");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Info_TwoCallEquality_NameFieldMatches()
    {
        // Non-memoized: two successive calls produce independently constructed instances
        // with equal field values. Assert equality on each individual field.
        var result = Run("let a = os.info(); let b = os.info(); let result = a.name == b.name;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Info_TwoCallEquality_EndiannessFieldMatches()
    {
        var result = Run("let a = os.info(); let b = os.info(); let result = a.endianness == b.endianness;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Info_TwoCallEquality_ArchFieldMatches()
    {
        var result = Run("let a = os.info(); let b = os.info(); let result = a.arch == b.arch;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Info_TwoCallEquality_IsUnixFieldMatches()
    {
        var result = Run("let a = os.info(); let b = os.info(); let result = a.isUnix == b.isUnix;");
        Assert.Equal(true, result);
    }

    // =========================================================================
    // env.os / env.arch absence
    // =========================================================================

    [Fact]
    public void EnvOs_AbsentFromStdlibMetadata()
    {
        bool hasEnvOs = StdlibRegistry.GetNamespaceMembers("env").Any(m => m.Name == "os")
            || StdlibRegistry.GetNamespaceDataMembers("env").Any(m => m.Name == "os");
        Assert.False(hasEnvOs, "env.os should not appear in stdlib metadata after removal");
    }

    [Fact]
    public void EnvArch_AbsentFromStdlibMetadata()
    {
        bool hasEnvArch = StdlibRegistry.GetNamespaceMembers("env").Any(m => m.Name == "arch")
            || StdlibRegistry.GetNamespaceDataMembers("env").Any(m => m.Name == "arch");
        Assert.False(hasEnvArch, "env.arch should not appear in stdlib metadata after removal");
    }

    [Fact]
    public void EnvOs_RaisesRuntimeError()
    {
        // env.os was removed in the os-namespace change; accessing it must raise a RuntimeError.
        RunExpectingError("let result = env.os;");
    }

    [Fact]
    public void EnvArch_RaisesRuntimeError()
    {
        // env.arch was removed in the os-namespace change; accessing it must raise a RuntimeError.
        RunExpectingError("let result = env.arch;");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Returns the expected <c>os.name()</c> value for the current host, derived
    /// from the same .NET APIs used by <c>OsBuiltIns.Name()</c>.
    /// </summary>
    private static string GetExpectedPlatformName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS())   return "macos";
        if (OperatingSystem.IsAndroid()) return "android";
        if (OperatingSystem.IsIOS())     return "ios";
        if (OperatingSystem.IsTvOS())    return "tvos";
        if (OperatingSystem.IsWatchOS()) return "watchos";
        if (OperatingSystem.IsFreeBSD()) return "freebsd";
        if (OperatingSystem.IsBrowser()) return "browser";
        if (OperatingSystem.IsLinux())   return "linux";
        return "unknown";
    }
}
