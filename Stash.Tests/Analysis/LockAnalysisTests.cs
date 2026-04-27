using Stash.Analysis;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for static analysis rules SA0811–SA0812 emitted by the <c>lock</c> statement.
/// </summary>
public class LockAnalysisTests : AnalysisTestBase
{
    // =========================================================================
    // SA0811 — Empty lock body
    // =========================================================================

    [Fact]
    public void SA0811_LockBodyEmpty_EmitsWarning()
    {
        var diagnostics = Validate("""lock "/var/run/test.lock" { }""");

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0811" &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0811_LockBodyNonEmpty_NoWarning()
    {
        var diagnostics = Validate("""lock "/var/run/test.lock" { let x = 1; }""");

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0811");
    }

    // =========================================================================
    // SA0812 — Nested lock on same path
    // =========================================================================

    [Fact]
    public void SA0812_NestedLockSamePath_EmitsError()
    {
        var diagnostics = Validate("""
            lock "/var/run/test.lock" {
                lock "/var/run/test.lock" {
                    let x = 1;
                }
            }
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0812" &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0812_NestedLockDifferentPaths_NoError()
    {
        var diagnostics = Validate("""
            lock "/var/run/lock1.lock" {
                lock "/var/run/lock2.lock" {
                    let x = 1;
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0812");
    }

    [Fact]
    public void SA0812_SequentialLocksOnSamePath_NoError()
    {
        // Sequential (not nested) locks on the same path are fine
        var diagnostics = Validate("""
            lock "/var/run/test.lock" {
                let x = 1;
            }
            lock "/var/run/test.lock" {
                let y = 2;
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0812");
    }
}
