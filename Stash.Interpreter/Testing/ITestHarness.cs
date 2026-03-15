namespace Stash.Testing;

using System;
using Stash.Common;

/// <summary>
/// Interface for test harness hooks called during test execution.
/// When no test harness is attached, these methods are never called (null check).
/// Mirrors the <see cref="Stash.Debugging.IDebugger"/> pattern for zero-overhead
/// when not testing.
/// </summary>
public interface ITestHarness
{
    /// <summary>
    /// Called when a test case begins execution.
    /// </summary>
    void OnTestStart(string name, SourceSpan span);

    /// <summary>
    /// Called when a test case passes (no assertion failures, no exceptions).
    /// </summary>
    void OnTestPass(string name, TimeSpan duration);

    /// <summary>
    /// Called when a test case fails (assertion failure or unhandled exception).
    /// </summary>
    void OnTestFail(string name, string message, SourceSpan? span, TimeSpan duration);

    /// <summary>
    /// Called when a test case is skipped.
    /// </summary>
    void OnTestSkip(string name, string? reason);

    /// <summary>
    /// Called when a describe() group begins.
    /// </summary>
    void OnSuiteStart(string name);

    /// <summary>
    /// Called when a describe() group ends.
    /// </summary>
    void OnSuiteEnd(string name, int passed, int failed, int skipped);

    /// <summary>
    /// Called when all tests have finished executing.
    /// Returns true if all tests passed, false otherwise.
    /// </summary>
    void OnRunComplete(int passed, int failed, int skipped);

    /// <summary>
    /// Gets the number of tests that have passed so far.
    /// </summary>
    int PassedCount => 0;

    /// <summary>
    /// Gets the number of tests that have failed so far.
    /// </summary>
    int FailedCount => 0;

    /// <summary>
    /// Gets the number of tests that have been skipped so far.
    /// </summary>
    int SkippedCount => 0;
}
