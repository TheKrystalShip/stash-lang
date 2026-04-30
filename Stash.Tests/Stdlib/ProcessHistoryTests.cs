using System;
using System.Collections.Generic;
using System.IO;
using Stash.Cli.History;
using Stash.Runtime;
using Stash.Stdlib.BuiltIns;

namespace Stash.Tests.Stdlib;

[CollectionDefinition("ProcessHistoryHandlers", DisableParallelization = true)]
public sealed class ProcessHistoryHandlersCollection { }

/// <summary>
/// Integration tests for the <c>process.historyList</c>, <c>process.historyClear</c>, and
/// <c>process.historyAdd</c> built-in functions (spec §12.6).
/// Each test wires a real <see cref="HistoryFileWriter"/> to the static delegate slots on
/// <see cref="ProcessBuiltIns"/> so the tests exercise the full path from Stash source through
/// the VM to the file layer.
/// </summary>
[Collection("ProcessHistoryHandlers")]
public sealed class ProcessHistoryTests : Stash.Tests.Interpreting.StashTestBase, IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;
    private readonly HistoryFileWriter _writer;

    public ProcessHistoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "history");
        _writer = new HistoryFileWriter(_path, 1000, Array.Empty<string>(), TextWriter.Null);

        ProcessBuiltIns.HistoryListProvider = () => _writer.Snapshot();
        ProcessBuiltIns.HistoryClearHandler = () => _writer.Clear();
        ProcessBuiltIns.HistoryAddHandler = e => _writer.Append(e);
    }

    public void Dispose()
    {
        ProcessBuiltIns.HistoryListProvider = null;
        ProcessBuiltIns.HistoryClearHandler = null;
        ProcessBuiltIns.HistoryAddHandler = null;
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // =========================================================================
    // process.historyList
    // =========================================================================

    [Fact]
    public void HistoryList_Empty_ReturnsEmptyArray()
    {
        var result = Run("let result = process.historyList();");

        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void HistoryAdd_ThenList_ReturnsAddedEntry()
    {
        RunStatements("process.historyAdd(\"foo\");");

        var result = Run("let result = process.historyList();");

        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        Assert.Equal("foo", list[0]);
    }

    [Fact]
    public void HistoryList_ReturnsCopy_MutationDoesNotAffectHistory()
    {
        RunStatements("process.historyAdd(\"real-entry\");");

        // Mutate the returned array in Stash and then query again
        RunStatements("""
            let h = process.historyList();
            arr.push(h, "bogus");
            """);

        var result = Run("let result = process.historyList();");

        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        Assert.DoesNotContain("bogus", list);
    }

    // =========================================================================
    // process.historyClear
    // =========================================================================

    [Fact]
    public void HistoryClear_EmptiesList()
    {
        RunStatements("process.historyAdd(\"a\"); process.historyAdd(\"b\");");

        RunStatements("process.historyClear();");

        var result = Run("let result = process.historyList();");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    // =========================================================================
    // process.historyAdd — validation
    // =========================================================================

    [Fact]
    public void HistoryAdd_EmptyString_ThrowsValueError()
    {
        var ex = RunCapturingError("""
            try {
                process.historyAdd("");
            } catch (e) {
                throw e;
            }
            """);

        Assert.Equal(StashErrorTypes.ValueError, ex.ErrorType);
    }

    [Fact]
    public void HistoryAdd_WhitespaceOnly_ThrowsValueError()
    {
        var ex = RunCapturingError("""
            try {
                process.historyAdd("   ");
            } catch (e) {
                throw e;
            }
            """);

        Assert.Equal(StashErrorTypes.ValueError, ex.ErrorType);
    }

    [Fact]
    public void HistoryAdd_LeadingSpaceLine_AcceptedButNotStored()
    {
        // Leading-space rule: the call succeeds (no error) but entry is silently discarded
        RunStatements("process.historyAdd(\" secret\");");

        var result = Run("let result = process.historyList();");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    // =========================================================================
    // Null handler (persistence disabled) behavior
    // =========================================================================

    [Fact]
    public void HistoryList_HandlersNull_ReturnsEmpty()
    {
        ProcessBuiltIns.HistoryListProvider = null;

        var result = Run("let result = process.historyList();");

        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void HistoryClear_HandlersNull_NoOp()
    {
        ProcessBuiltIns.HistoryClearHandler = null;

        // Should not throw
        RunStatements("process.historyClear();");
    }

    [Fact]
    public void HistoryAdd_HandlersNull_NoOp()
    {
        ProcessBuiltIns.HistoryAddHandler = null;

        // Should not throw — just returns null silently
        RunStatements("process.historyAdd(\"foo\");");
    }
}
