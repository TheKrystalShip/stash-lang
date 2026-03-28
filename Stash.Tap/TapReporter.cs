namespace Stash.Tap;

using System;
using System.IO;
using Stash.Runtime;
using Stash.Common;

/// <summary>
/// Test harness that produces TAP (Test Anything Protocol) version 14 output.
/// </summary>
public class TapReporter : ITestHarness
{
    private readonly TextWriter _output;
    private int _testNumber;
    private int _totalPassed;
    private int _totalFailed;
    private int _totalSkipped;
    private bool _headerWritten;

    public int PassedCount => _totalPassed;
    public int FailedCount => _totalFailed;
    public int SkippedCount => _totalSkipped;

    public TapReporter(TextWriter? output = null)
    {
        _output = output ?? Console.Out;
    }

    private void EnsureHeader()
    {
        if (!_headerWritten)
        {
            _output.WriteLine("TAP version 14");
            _headerWritten = true;
        }
    }

    public void OnTestStart(string name, SourceSpan span)
    {
        EnsureHeader();
        _testNumber++;
    }

    public void OnTestPass(string name, TimeSpan duration)
    {
        _totalPassed++;
        _output.WriteLine($"ok {_testNumber} - {name}");
    }

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

    public void OnTestSkip(string name, string? reason)
    {
        EnsureHeader();
        _testNumber++;
        _totalSkipped++;
        string directive = reason is not null ? $" # SKIP {reason}" : " # SKIP";
        _output.WriteLine($"ok {_testNumber} - {name}{directive}");
    }

    public void OnSuiteStart(string name)
    {
        EnsureHeader();
        _output.WriteLine($"# {name}");
    }

    public void OnSuiteEnd(string name, int passed, int failed, int skipped)
    {
        // TAP doesn't have a formal suite-end marker; the comment is optional
    }

    public void OnRunComplete(int passed, int failed, int skipped)
    {
        _output.WriteLine($"1..{_testNumber}");
    }

    public void OnTestDiscovered(string name, SourceSpan span)
    {
        EnsureHeader();
        _output.WriteLine($"# discovered: {name} [{span.File}:{span.StartLine}:{span.StartColumn}]");
    }

    private static string EscapeYaml(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
