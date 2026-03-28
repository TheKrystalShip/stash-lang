namespace Stash.Runtime;

using System.Collections.Generic;

/// <summary>
/// Test framework state: harness, describe nesting, filtering, lifecycle hooks.
/// Used by TestBuiltIns and AssertBuiltins.
/// </summary>
public interface ITestContext
{
    ITestHarness? TestHarness { get; set; }
    string? CurrentDescribe { get; set; }
    string[]? TestFilter { get; set; }
    bool DiscoveryMode { get; set; }
    List<List<IStashCallable>> BeforeEachHooks { get; }
    List<List<IStashCallable>> AfterEachHooks { get; }
    List<List<IStashCallable>> AfterAllHooks { get; }
}
