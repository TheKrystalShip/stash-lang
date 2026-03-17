namespace Stash.Testing;

using System;
using System.IO;
using Stash.Common;

/// <summary>
/// Test harness that produces TAP (Test Anything Protocol) version 14 output.
/// </summary>
public class TapReporter : ITestHarness
{
    /// <summary>The text writer for TAP output.</summary>
    private readonly TextWriter _output;
    /// <summary>The running test number, incremented for each test started.</summary>
    private int _testNumber;
    /// <summary>Total number of tests that passed.</summary>
    private int _totalPassed;
    /// <summary>Total number of tests that failed.</summary>
    private int _totalFailed;
    /// <summary>Total number of tests that were skipped.</summary>
    private int _totalSkipped;
    /// <summary>Whether the TAP version header has been written yet.</summary>
    private bool _headerWritten;

    /// <summary>Gets the number of passed tests.</summary>
    public int Passed => _totalPassed;
    /// <summary>Gets the number of failed tests.</summary>
    public int Failed => _totalFailed;
    /// <summary>Gets the number of skipped tests.</summary>
    public int Skipped => _totalSkipped;

    public int PassedCount => _totalPassed;
    public int FailedCount => _totalFailed;
    public int SkippedCount => _totalSkipped;

    /// <summary>Creates a new TAP reporter.</summary>
    /// <param name="output">The text writer for output. Defaults to <see cref="Console.Out"/> if null.</param>
    public TapReporter(TextWriter? output = null)
    {
        _output = output ?? Console.Out;
    }

    /// <summary>Writes the TAP version header if it hasn't been written yet.</summary>
    private void EnsureHeader()
    {
        if (!_headerWritten)
        {
            _output.WriteLine("TAP version 14");
            _headerWritten = true;
        }
    }

    /// <inheritdoc />
    public void OnTestStart(string name, SourceSpan span)
    {
        EnsureHeader();
        _testNumber++;
    }

    /// <inheritdoc />
    public void OnTestPass(string name, TimeSpan duration)
    {
        _totalPassed++;
        _output.WriteLine($"ok {_testNumber} - {name}");
    }

    /// <inheritdoc />
    public void OnTestFail(string name, string message, SourceSpan? span, TimeSpan duration)
    {
        _totalFailed++;
        _output.WriteLine($"not ok {_testNumber} - {name}");
        _output.WriteLine("  ---");
        _output.WriteLine($"  message: \"{EscapeYaml(message)}\"");
        _output.WriteLine("  severity: fail");
        if (span is not null)
        {
            _output.WriteLine("  at:");
            _output.WriteLine($"    file: {span.File}");
            _output.WriteLine($"    line: {span.StartLine}");
            _output.WriteLine($"    column: {span.StartColumn}");
        }
        _output.WriteLine("  ...");
    }

    /// <inheritdoc />
    public void OnTestSkip(string name, string? reason)
    {
        EnsureHeader();
        _testNumber++;
        _totalSkipped++;
        string directive = reason is not null ? $" # SKIP {reason}" : " # SKIP";
        _output.WriteLine($"ok {_testNumber} - {name}{directive}");
    }

    /// <inheritdoc />
    public void OnSuiteStart(string name)
    {
        EnsureHeader();
        _output.WriteLine($"# {name}");
    }

    /// <inheritdoc />
    public void OnSuiteEnd(string name, int passed, int failed, int skipped)
    {
        // TAP doesn't have a formal suite-end marker; the comment is optional
    }

    /// <inheritdoc />
    public void OnRunComplete(int passed, int failed, int skipped)
    {
        _output.WriteLine($"1..{_testNumber}");
    }

    /// <inheritdoc />
    public void OnTestDiscovered(string name, SourceSpan span)
    {
        EnsureHeader();
        _output.WriteLine($"# discovered: {name} [{span.File}:{span.StartLine}:{span.StartColumn}]");
    }

    /// <summary>Escapes backslash and double-quote characters for YAML string output.</summary>
    private static string EscapeYaml(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
