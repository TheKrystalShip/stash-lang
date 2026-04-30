using System;
using System.Runtime.InteropServices;

namespace Stash.Tests.Stdlib;

/// <summary>
/// Tests for the global <c>Signal</c> enum added in Phase A of the Process Namespace
/// Decomposition. Covers enum member accessibility, identity, and <c>process.signal</c>
/// integration.
/// </summary>
public class SignalEnumTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // Signal enum member identity
    // =========================================================================

    [Fact]
    public void Signal_Term_IsSignalEnum()
    {
        var result = Run("let result = Signal.Term is Signal;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Signal_Kill_IsSignalEnum()
    {
        var result = Run("let result = Signal.Kill is Signal;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Signal_Hup_IsSignalEnum()
    {
        var result = Run("let result = Signal.Hup is Signal;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Signal_Int_IsSignalEnum()
    {
        var result = Run("let result = Signal.Int is Signal;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Signal_Quit_IsSignalEnum()
    {
        var result = Run("let result = Signal.Quit is Signal;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Signal_Usr1_IsSignalEnum()
    {
        var result = Run("let result = Signal.Usr1 is Signal;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Signal_Usr2_IsSignalEnum()
    {
        var result = Run("let result = Signal.Usr2 is Signal;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Signal_AllMembersAreDistinct()
    {
        var result = Run(
            "let result = Signal.Hup != Signal.Int &&" +
            " Signal.Int != Signal.Quit &&" +
            " Signal.Quit != Signal.Kill &&" +
            " Signal.Kill != Signal.Usr1 &&" +
            " Signal.Usr1 != Signal.Usr2 &&" +
            " Signal.Usr2 != Signal.Term;");
        Assert.Equal(true, result);
    }

    // =========================================================================
    // process.signal integration
    // =========================================================================

    [Fact]
    public void Signal_Term_AcceptedByProcessSignal()
    {
        // POSIX only: spawn a long-running process and send SIGTERM via enum constant.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var result = Run(
            "let h = process.spawn(\"sleep 60\");" +
            "let ok = process.signal(h, Signal.Term);" +
            "process.wait(h);" +
            "let result = ok;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Signal_Int_AcceptsRawInteger()
    {
        // POSIX only: verify that process.signal still accepts a raw integer (15).
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var result = Run(
            "let h = process.spawn(\"sleep 60\");" +
            "let ok = process.signal(h, 15);" +
            "process.wait(h);" +
            "let result = ok;");
        Assert.Equal(true, result);
    }
}
