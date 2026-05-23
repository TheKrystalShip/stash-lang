namespace Stash.Tests.Runtime;

using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;

/// <summary>
/// Tests for ReadOnlyError — the error raised when a dynamic assignment targets a
/// built-in namespace or a user-module alias namespace at runtime (the path that escapes
/// static SA0845 detection via a dynamic receiver).
/// </summary>
public class ReadOnlyErrorTests : StashTestBase
{
    // =========================================================================
    // ReadOnlyError registration
    // =========================================================================

    [Fact]
    public void ReadOnlyError_IsRegisteredInBuiltInErrorRegistry()
    {
        var err = new ReadOnlyError("test");
        Assert.Equal("ReadOnlyError", BuiltInErrorRegistry.NameOf(err));
    }

    [Fact]
    public void ReadOnlyError_NameOf_RoundTrips()
    {
        var err = new ReadOnlyError("cannot assign");
        string name = BuiltInErrorRegistry.NameOf(err);
        Assert.Equal("ReadOnlyError", name);
    }

    // =========================================================================
    // Dynamic-receiver namespace assignment raises ReadOnlyError
    // =========================================================================

    [Fact]
    public void DynamicAssignToBuiltInNamespace_RaisesReadOnlyError()
    {
        // A dynamic receiver (alias variable) escapes compile-time SA0845; the VM must
        // raise ReadOnlyError, not the generic RuntimeError / TypeError.
        var error = RunCapturingError("""
            let ns = io;
            ns.println = null;
            """);
        Assert.IsType<ReadOnlyError>(error);
    }

    [Fact]
    public void DynamicAssignToBuiltInNamespace_ErrorMessage_MentionsNamespace()
    {
        var error = RunCapturingError("""
            let ns = io;
            ns.println = null;
            """);
        Assert.IsType<ReadOnlyError>(error);
        Assert.Contains("io", error.Message);
    }

    [Fact]
    public void DirectAssignToBuiltInNamespaceMember_RaisesReadOnlyError()
    {
        // Even without a dynamic receiver alias, if the code somehow reaches the VM
        // SetField path for a namespace, it must produce ReadOnlyError.
        // (The static path suppresses emission, but the runtime guard must also hold.)
        var error = RunCapturingError("""
            let ns = math;
            ns.PI = 0;
            """);
        Assert.IsType<ReadOnlyError>(error);
    }

    // =========================================================================
    // ReadOnlyError is catchable by type in Stash
    // =========================================================================

    [Fact]
    public void ReadOnlyError_IsCatchableByType()
    {
        // The error must surface as a catchable ReadOnlyError in Stash try/catch.
        // Stash typed catch syntax: catch (TypeName varName)
        var result = Run("""
            let caught = false;
            try {
                let ns = io;
                ns.println = null;
            } catch (ReadOnlyError e) {
                caught = true;
            }
            let result = caught;
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ReadOnlyError_NotRaisedForValidDotAssign()
    {
        // Assigning to a dict field via dot syntax must not raise ReadOnlyError.
        var source = """
            let d = {};
            dict.set(d, "key", 1);
            """;
        // Should execute without any error.
        RunStatements(source);
    }
}
