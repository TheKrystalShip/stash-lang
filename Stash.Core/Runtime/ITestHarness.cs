namespace Stash.Runtime;

using System;
using Stash.Common;

/// <summary>
/// Interface for test harness hooks called during test execution.
/// When no test harness is attached, these methods are never called (null check).
/// </summary>
public interface ITestHarness
{
    void OnTestStart(string name, SourceSpan span);
    void OnTestPass(string name, TimeSpan duration);
    void OnTestFail(string name, string message, SourceSpan? span, TimeSpan duration);
    void OnTestSkip(string name, string? reason);
    void OnSuiteStart(string name);
    void OnSuiteEnd(string name, int passed, int failed, int skipped);
    void OnRunComplete(int passed, int failed, int skipped);
    int PassedCount => 0;
    int FailedCount => 0;
    int SkippedCount => 0;
    void OnTestDiscovered(string name, SourceSpan span) { }
}
