namespace Stash.Tests.Interpreting.Async.ErrorPropagation;

using Stash.Runtime;
using Stash.Tests.Interpreting;

/// <summary>
/// Contract pairing: fail-fast vs collect-all.
///
/// fail-fast combinators (awaitAny, all, race): throw on first failure, preserving
///   the original error type.
/// collect-all combinator (awaitAll): never throws; per-element errors become StashError
///   values with original type preserved.
///
/// Both halves are asserted here as a single behavioral contract so a change that shifts
/// one side trips this class explicitly.
/// </summary>
public class CombinatorPairingTests : StashTestBase
{
    // ── collect-all side — awaitAll never throws ──────────────────────────────

    [Fact]
    public void CombinatorPairing_AwaitAll_CollectsAll_DoesNotThrow()
    {
        RunStatements(@"
let f = task.run(() => { throw TypeError { message: ""nope"" }; });
let results = task.awaitAll([f]);
");
    }

    [Fact]
    public void CombinatorPairing_AwaitAll_FailureElement_HasOriginalType()
    {
        var result = Run(@"
let f = task.run(() => { throw TypeError { message: ""nope"" }; });
let results = task.awaitAll([f]);
let result = results[0].type;
");
        Assert.Equal("TypeError", result);
    }

    // ── fail-fast side — awaitAny throws original type ────────────────────────

    [Fact]
    public void CombinatorPairing_AwaitAny_FailFast_ThrowsOriginalError()
    {
        // throw TypeError { ... } in Stash produces a UserRuntimeError whose ErrorType == "TypeError".
        // The pairing contract is about the Stash-visible type surviving, not CLR identity.
        var error = RunCapturingError(@"
let f = task.run(() => { throw TypeError { message: ""nope"" }; });
task.awaitAny([f]);
");
        Assert.IsAssignableFrom<RuntimeError>(error);
        Assert.Equal("TypeError", error.ErrorType);
    }

    [Fact]
    public void CombinatorPairing_AwaitAny_FailFast_PreservesMessage()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw TypeError { message: ""nope"" }; });
task.awaitAny([f]);
");
        Assert.Equal("TypeError", error.ErrorType);
        Assert.Equal("nope", error.Message);
    }

    // ── fail-fast side — task.all throws original type ────────────────────────

    [Fact]
    public void CombinatorPairing_All_FailFast_ThrowsOriginalError()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw TypeError { message: ""nope"" }; });
let combined = task.all([f]);
await combined;
");
        Assert.Equal("TypeError", error.ErrorType);
    }

    // ── fail-fast side — task.race throws original type ───────────────────────

    [Fact]
    public void CombinatorPairing_Race_FailFast_ThrowsOriginalError()
    {
        var error = RunCapturingError(@"
let f = task.run(() => { throw TypeError { message: ""nope"" }; });
let racing = task.race([f]);
await racing;
");
        Assert.Equal("TypeError", error.ErrorType);
    }

    // ── cross-pairing: same error, different combinator behavior ─────────────

    /// <summary>
    /// The clearest expression of the pairing contract: same throwing task, same error type,
    /// but awaitAll collects (no throw) while awaitAny throws. Both in one test.
    /// </summary>
    [Fact]
    public void CombinatorPairing_SameError_AwaitAllCollects_AwaitAnyThrows()
    {
        // awaitAll side — does not throw
        RunStatements(@"
let f1 = task.run(() => { throw TypeError { message: ""x"" }; });
let results = task.awaitAll([f1]);
");

        // awaitAny side — throws
        var error = RunCapturingError(@"
let f2 = task.run(() => { throw TypeError { message: ""x"" }; });
task.awaitAny([f2]);
");
        Assert.Equal("TypeError", error.ErrorType);
    }
}
