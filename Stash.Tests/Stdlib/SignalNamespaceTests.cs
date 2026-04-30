namespace Stash.Tests.Stdlib;

/// <summary>
/// Tests for the <c>signal</c> namespace: signal.on, signal.off,
/// and the deprecated sys.onSignal / sys.offSignal shims.
/// </summary>
public class SignalNamespaceTests : Stash.Tests.Interpreting.StashTestBase
{
    [Fact]
    public void SignalOn_Term_RegistersWithoutThrowing()
    {
        // signal.on stores the handler. PosixSignalRegistration may not fire
        // in test context, but the call itself must not throw.
        RunStatements("signal.on(Signal.Term, () => null);");
    }

    [Fact]
    public void SignalOff_Term_RemovesWithoutThrowing()
    {
        // off on a signal with no existing handler is a no-op — must not throw.
        RunStatements("signal.off(Signal.Term);");
    }

    [Fact]
    public void SysOnSignal_DeprecatedAlias_RegistersWithoutThrowing()
    {
        // The deprecated sys.onSignal shim delegates to SignalImpl.OnSignal.
        RunStatements("sys.onSignal(Signal.Hup, () => null);");
    }

    [Fact]
    public void SysOffSignal_DeprecatedAlias_RemovesWithoutThrowing()
    {
        // The deprecated sys.offSignal shim delegates to SignalImpl.OffSignal.
        RunStatements("sys.offSignal(Signal.Hup);");
    }
}
