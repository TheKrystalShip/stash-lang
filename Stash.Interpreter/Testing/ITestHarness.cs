namespace Stash.Testing;

/// <summary>
/// Backwards-compatibility shim — ITestHarness now lives in Stash.Runtime.
/// </summary>
public interface ITestHarness : Stash.Runtime.ITestHarness { }
