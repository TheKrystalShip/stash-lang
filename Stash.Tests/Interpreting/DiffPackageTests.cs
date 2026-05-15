using System.IO;
using Stash.Runtime;

namespace Stash.Tests.Interpreting;

/// <summary>
/// Tests for the @stash/diff package (pure-Stash, lives at
/// examples/packages/diff/). Each test compiles a small Stash script that
/// imports the package by relative path from a synthetic source location
/// next to examples/, then asserts on the returned value (typically the
/// `result` variable from the script).
/// </summary>
public class DiffPackageTests : StashTestBase
{
    // ── Constants — script-level conveniences ─────────────────────────────────

    /// <summary>Absolute path to the in-repo @stash/diff package root.</summary>
    private static readonly string DiffPackageRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "examples", "packages", "diff"));

    /// <summary>Synthetic "current file" so the VM's import resolver finds
    /// examples/packages/diff/index.stash relative to it. The file does not
    /// need to exist — only its directory is used for resolution.</summary>
    private static readonly string PseudoScriptPath =
        Path.GetFullPath(Path.Combine(DiffPackageRoot, "..", "..", "_diff_tests_pseudo.stash"));

    private const string ImportDiff =
        "import \"packages/diff/index.stash\" as diff;\n";

    private const string LineEqual  = "Op.EQUAL";
    private const string LineInsert = "Op.INSERT";
    private const string LineDelete = "Op.DELETE";

    private static object? RunDiff(string body)
        => RunWithFile(ImportDiff + body, PseudoScriptPath);

    private static string RunDiffString(string body)
        => (string)RunDiff(body)!;

    private static long RunDiffInt(string body)
        => Convert.ToInt64(RunDiff(body));

    private static bool RunDiffBool(string body)
        => (bool)RunDiff(body)!;

    // ── 1. Identity — diffLines(x, x) is equal ────────────────────────────────

    [Fact]
    public void DiffLines_IdenticalInputs_IsEqual()
    {
        Assert.True(RunDiffBool("let r = diff.diff.diffLines(\"foo\\nbar\\n\", \"foo\\nbar\\n\", null); let result = r.equal;"));
    }

    [Fact]
    public void DiffLines_IdenticalInputs_ZeroHunks()
    {
        Assert.Equal(0L, RunDiffInt("let r = diff.diff.diffLines(\"foo\\nbar\\n\", \"foo\\nbar\\n\", null); let result = len(r.hunks);"));
    }

    [Fact]
    public void DiffLines_IdenticalInputs_Patience_IsEqual()
    {
        string body = @"
let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.PATIENCE,
  contextLines: 3, whitespace: diff.types.WhitespaceMode.NONE,
  ignoreCase: false, ignoreBlankLines: false, preserveLineEndings: false,
  maxLines: 0, aLabel: ""a"", bLabel: ""b""
};
let r = diff.diff.diffLines(""foo\nbar\nbaz\n"", ""foo\nbar\nbaz\n"", opts);
let result = r.equal;";
        Assert.True(RunDiffBool(body));
    }

    // ── 2. Fully disjoint — every old line DELETE, every new line INSERT ──────

    [Fact]
    public void DiffLines_FullyDisjoint_CountsMatch()
    {
        long ins = RunDiffInt("let r = diff.diff.diffLines(\"a\\nb\\nc\\n\", \"x\\ny\\nz\\n\", null); let result = r.insertions;");
        long del = RunDiffInt("let r = diff.diff.diffLines(\"a\\nb\\nc\\n\", \"x\\ny\\nz\\n\", null); let result = r.deletions;");
        Assert.Equal(3L, ins);
        Assert.Equal(3L, del);
    }

    // ── 3. Single-line change in the middle ──────────────────────────────────

    [Fact]
    public void DiffLines_SingleMiddleChange_OneHunkOneInsertOneDelete()
    {
        long hunks = RunDiffInt("let r = diff.diff.diffLines(\"foo\\nbar\\nqux\\n\", \"foo\\nbaz\\nqux\\n\", null); let result = len(r.hunks);");
        long ins = RunDiffInt("let r = diff.diff.diffLines(\"foo\\nbar\\nqux\\n\", \"foo\\nbaz\\nqux\\n\", null); let result = r.insertions;");
        long del = RunDiffInt("let r = diff.diff.diffLines(\"foo\\nbar\\nqux\\n\", \"foo\\nbaz\\nqux\\n\", null); let result = r.deletions;");
        Assert.Equal(1L, hunks);
        Assert.Equal(1L, ins);
        Assert.Equal(1L, del);
    }

    [Fact]
    public void DiffLines_SingleInsertInMiddle_HasOneInsertZeroDelete()
    {
        long ins = RunDiffInt("let r = diff.diff.diffLines(\"a\\nc\\n\", \"a\\nb\\nc\\n\", null); let result = r.insertions;");
        long del = RunDiffInt("let r = diff.diff.diffLines(\"a\\nc\\n\", \"a\\nb\\nc\\n\", null); let result = r.deletions;");
        Assert.Equal(1L, ins);
        Assert.Equal(0L, del);
    }

    [Fact]
    public void DiffLines_SingleDeleteInMiddle_HasOneDeleteZeroInsert()
    {
        long ins = RunDiffInt("let r = diff.diff.diffLines(\"a\\nb\\nc\\n\", \"a\\nc\\n\", null); let result = r.insertions;");
        long del = RunDiffInt("let r = diff.diff.diffLines(\"a\\nb\\nc\\n\", \"a\\nc\\n\", null); let result = r.deletions;");
        Assert.Equal(0L, ins);
        Assert.Equal(1L, del);
    }

    // ── 4. Multiple non-adjacent hunks ───────────────────────────────────────

    [Fact]
    public void DiffLines_FarApartChanges_DefaultContext_TwoHunks()
    {
        // contextLines=1; the two changes are 5 EQUAL lines apart so the
        // 1-line windows do not overlap.
        string body = @"
let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.MYERS,
  contextLines: 1, whitespace: diff.types.WhitespaceMode.NONE,
  ignoreCase: false, ignoreBlankLines: false, preserveLineEndings: false,
  maxLines: 0, aLabel: ""a"", bLabel: ""b""
};
let a = ""1\n2\n3\n4\n5\n6\n7\n"";
let b = ""X\n2\n3\n4\n5\n6\nY\n"";
let r = diff.diff.diffLines(a, b, opts);
let result = len(r.hunks);";
        Assert.Equal(2L, RunDiffInt(body));
    }

    [Fact]
    public void DiffLines_AdjacentChanges_LargeContext_MergesIntoOneHunk()
    {
        // contextLines=10 covers everything between the two changes.
        string body = @"
let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.MYERS,
  contextLines: 10, whitespace: diff.types.WhitespaceMode.NONE,
  ignoreCase: false, ignoreBlankLines: false, preserveLineEndings: false,
  maxLines: 0, aLabel: ""a"", bLabel: ""b""
};
let a = ""1\n2\n3\n4\n5\n"";
let b = ""X\n2\n3\n4\nY\n"";
let r = diff.diff.diffLines(a, b, opts);
let result = len(r.hunks);";
        Assert.Equal(1L, RunDiffInt(body));
    }

    // ── 5. Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void DiffLines_EmptyA_AllInsert()
    {
        long ins = RunDiffInt("let r = diff.diff.diffLines(\"\", \"a\\nb\\nc\\n\", null); let result = r.insertions;");
        long del = RunDiffInt("let r = diff.diff.diffLines(\"\", \"a\\nb\\nc\\n\", null); let result = r.deletions;");
        Assert.Equal(3L, ins);
        Assert.Equal(0L, del);
    }

    [Fact]
    public void DiffLines_EmptyB_AllDelete()
    {
        long ins = RunDiffInt("let r = diff.diff.diffLines(\"a\\nb\\nc\\n\", \"\", null); let result = r.insertions;");
        long del = RunDiffInt("let r = diff.diff.diffLines(\"a\\nb\\nc\\n\", \"\", null); let result = r.deletions;");
        Assert.Equal(0L, ins);
        Assert.Equal(3L, del);
    }

    [Fact]
    public void DiffLines_BothEmpty_IsEqual()
    {
        Assert.True(RunDiffBool("let r = diff.diff.diffLines(\"\", \"\", null); let result = r.equal;"));
    }

    [Fact]
    public void DiffLines_NoTrailingNewline_RecordedOnResult()
    {
        bool aMiss = RunDiffBool("let r = diff.diff.diffLines(\"foo\\nbar\", \"foo\\nbar\\n\", null); let result = r.aMissingNewline;");
        bool bMiss = RunDiffBool("let r = diff.diff.diffLines(\"foo\\nbar\", \"foo\\nbar\\n\", null); let result = r.bMissingNewline;");
        Assert.True(aMiss);
        Assert.False(bMiss);
    }

    [Fact]
    public void DiffLines_CRLF_NormalizedToLF_NoChange()
    {
        // Same content, different line endings — should be equal.
        Assert.True(RunDiffBool("let r = diff.diff.diffLines(\"foo\\r\\nbar\\r\\n\", \"foo\\nbar\\n\", null); let result = r.equal;"));
    }

    // ── 6. Option flags ──────────────────────────────────────────────────────

    [Fact]
    public void DiffLines_IgnoreWhitespaceAll_TreatsCollapsedRunsEqual()
    {
        string body = @"
let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.MYERS,
  contextLines: 3, whitespace: diff.types.WhitespaceMode.IGNORE_ALL,
  ignoreCase: false, ignoreBlankLines: false, preserveLineEndings: false,
  maxLines: 0, aLabel: ""a"", bLabel: ""b""
};
let r = diff.diff.diffLines(""foo  bar\n"", ""foo bar\n"", opts);
let result = r.equal;";
        Assert.True(RunDiffBool(body));
    }

    [Fact]
    public void DiffLines_IgnoreWhitespaceTrailing_TreatsTrailingDifferenceEqual()
    {
        string body = @"
let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.MYERS,
  contextLines: 3, whitespace: diff.types.WhitespaceMode.IGNORE_TRAILING,
  ignoreCase: false, ignoreBlankLines: false, preserveLineEndings: false,
  maxLines: 0, aLabel: ""a"", bLabel: ""b""
};
let r = diff.diff.diffLines(""foo   \n"", ""foo\n"", opts);
let result = r.equal;";
        Assert.True(RunDiffBool(body));
    }

    [Fact]
    public void DiffLines_IgnoreCase_TreatsMixedCaseEqual()
    {
        string body = @"
let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.MYERS,
  contextLines: 3, whitespace: diff.types.WhitespaceMode.NONE,
  ignoreCase: true, ignoreBlankLines: false, preserveLineEndings: false,
  maxLines: 0, aLabel: ""a"", bLabel: ""b""
};
let r = diff.diff.diffLines(""Foo\nBar\n"", ""foo\nbar\n"", opts);
let result = r.equal;";
        Assert.True(RunDiffBool(body));
    }

    [Fact]
    public void DiffLines_IgnoreBlankLines_SkipsInterleavedBlanks()
    {
        string body = @"
let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.MYERS,
  contextLines: 3, whitespace: diff.types.WhitespaceMode.NONE,
  ignoreCase: false, ignoreBlankLines: true, preserveLineEndings: false,
  maxLines: 0, aLabel: ""a"", bLabel: ""b""
};
let r = diff.diff.diffLines(""foo\n\nbar\n"", ""foo\nbar\n\n"", opts);
let result = r.equal;";
        Assert.True(RunDiffBool(body));
    }

    [Fact]
    public void DiffLines_MaxLinesExceeded_ThrowsValueError()
    {
        string body = @"
let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.MYERS,
  contextLines: 3, whitespace: diff.types.WhitespaceMode.NONE,
  ignoreCase: false, ignoreBlankLines: false, preserveLineEndings: false,
  maxLines: 1, aLabel: ""a"", bLabel: ""b""
};
let result = diff.diff.diffLines(""a\nb\nc\n"", ""d\ne\nf\n"", opts).equal;";
        var ex = Assert.ThrowsAny<RuntimeError>(() => RunDiff(body));
        Assert.Contains("maxLines", ex.Message);
    }

    // ── 7. Algorithm parity — same counts for MYERS and PATIENCE ─────────────

    [Fact]
    public void DiffLines_MyersAndPatience_SameCounts()
    {
        // A small input where both algorithms must agree on insert/delete totals.
        string body = @"
let a = ""alpha\nbeta\ngamma\ndelta\nepsilon\n"";
let b = ""alpha\nbeta\nGAMMA\ndelta\nepsilon\n"";
let m = diff.diff.diffLines(a, b, null);
let pOpts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.PATIENCE,
  contextLines: 3, whitespace: diff.types.WhitespaceMode.NONE,
  ignoreCase: false, ignoreBlankLines: false, preserveLineEndings: false,
  maxLines: 0, aLabel: ""a"", bLabel: ""b""
};
let p = diff.diff.diffLines(a, b, pOpts);
let result = (m.insertions == p.insertions) && (m.deletions == p.deletions);";
        Assert.True(RunDiffBool(body));
    }

    // ── 8. Rendering round-trip ──────────────────────────────────────────────

    [Fact]
    public void RenderUnified_SingleMiddleChange_MatchesGnuDiffFormat()
    {
        // The canonical "foo/bar/qux -> foo/baz/qux" fixture from §5 of the spec.
        string body = @"
let r = diff.diff.diffLines(""foo\nbar\nqux\n"", ""foo\nbaz\nqux\n"", null);
let result = diff.render.renderUnified(r);";
        string expected =
            "--- a\n" +
            "+++ b\n" +
            "@@ -1,3 +1,3 @@\n" +
            " foo\n" +
            "-bar\n" +
            "+baz\n" +
            " qux\n";
        Assert.Equal(expected, RunDiffString(body));
    }

    [Fact]
    public void RenderUnified_EqualInputs_ReturnsEmptyString()
    {
        string body = "let r = diff.diff.diffLines(\"x\\ny\\n\", \"x\\ny\\n\", null); let result = diff.render.renderUnified(r);";
        Assert.Equal("", RunDiffString(body));
    }

    [Fact]
    public void RenderUnified_MissingTrailingNewline_EmitsMarker()
    {
        string body = @"
let r = diff.diff.diffLines(""foo\nbar"", ""foo\nbaz\n"", null);
let result = diff.render.renderUnified(r);";
        string output = RunDiffString(body);
        Assert.Contains("\\ No newline at end of file", output);
    }

    [Fact]
    public void RenderUnified_HunkHeaderUsesCommaSeparator()
    {
        string body = @"
let r = diff.diff.diffLines(""1\n2\n3\n"", ""1\nX\n3\n"", null);
let result = diff.render.renderUnified(r);";
        string output = RunDiffString(body);
        Assert.Contains("@@ -1,3 +1,3 @@", output);
    }

    [Fact]
    public void RenderUnified_SingleLineHunkHeader_OmitsCount()
    {
        // A 1-line hunk uses "@@ -N +M @@" rather than "@@ -N,1 +M,1 @@".
        string body = @"
let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.MYERS,
  contextLines: 0, whitespace: diff.types.WhitespaceMode.NONE,
  ignoreCase: false, ignoreBlankLines: false, preserveLineEndings: false,
  maxLines: 0, aLabel: ""a"", bLabel: ""b""
};
let r = diff.diff.diffLines(""1\n2\n3\n"", ""1\nX\n3\n"", opts);
let result = diff.render.renderUnified(r);";
        string output = RunDiffString(body);
        Assert.Contains("@@ -2 +2 @@", output);
    }

    // ── 9. Custom labels and diffFiles ───────────────────────────────────────

    [Fact]
    public void DiffLines_CustomLabels_AppearInHeader()
    {
        string body = @"
let opts = diff.types.DiffOptions {
  algorithm: diff.types.DiffAlgorithm.MYERS,
  contextLines: 3, whitespace: diff.types.WhitespaceMode.NONE,
  ignoreCase: false, ignoreBlankLines: false, preserveLineEndings: false,
  maxLines: 0, aLabel: ""before.txt"", bLabel: ""after.txt""
};
let r = diff.diff.diffLines(""a\n"", ""b\n"", opts);
let result = diff.render.renderUnified(r);";
        string output = RunDiffString(body);
        Assert.Contains("--- before.txt", output);
        Assert.Contains("+++ after.txt", output);
    }

    [Fact]
    public void DiffFiles_ReadsFromDisk_DefaultLabelsArePaths()
    {
        string dir = Path.Combine(Path.GetTempPath(), "stash-diff-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string pathA = Path.Combine(dir, "a.txt");
        string pathB = Path.Combine(dir, "b.txt");
        File.WriteAllText(pathA, "foo\nbar\n");
        File.WriteAllText(pathB, "foo\nbaz\n");

        try
        {
            string aLit = pathA.Replace("\\", "\\\\");
            string bLit = pathB.Replace("\\", "\\\\");
            string body = $@"
let r = diff.diff.diffFiles(""{aLit}"", ""{bLit}"", null);
let result = diff.render.renderUnified(r);";
            string output = RunDiffString(body);
            Assert.Contains("--- " + pathA, output);
            Assert.Contains("+++ " + pathB, output);
            Assert.Contains("-bar", output);
            Assert.Contains("+baz", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── 10. equal() convenience ──────────────────────────────────────────────

    [Fact]
    public void Equal_IdenticalInputs_True()
    {
        Assert.True(RunDiffBool("let result = diff.diff.equal(\"a\\nb\\n\", \"a\\nb\\n\", null);"));
    }

    [Fact]
    public void Equal_DifferentInputs_False()
    {
        Assert.False(RunDiffBool("let result = diff.diff.equal(\"a\\n\", \"b\\n\", null);"));
    }

    // ── 11. Colorize ─────────────────────────────────────────────────────────

    [Fact]
    public void Colorize_Disabled_ReturnsInputUnchanged()
    {
        string body = "let result = diff.render.colorize(\"--- a\\n+++ b\\n\", false);";
        Assert.Equal("--- a\n+++ b\n", RunDiffString(body));
    }

    [Fact]
    public void Colorize_Enabled_WrapsLinesWithAnsiEscapes()
    {
        string body = @"
let r = diff.diff.diffLines(""x\n"", ""y\n"", null);
let result = diff.render.colorize(diff.render.renderUnified(r), true);";
        string output = RunDiffString(body);
        // ESC = char 27
        Assert.Contains("[31m-x", output);
        Assert.Contains("[32m+y", output);
        Assert.Contains("[0m", output);
    }

    // ── 12. Hunk structure ───────────────────────────────────────────────────

    [Fact]
    public void Hunk_OldAndNewStart_AreOneBased()
    {
        long oldStart = RunDiffInt("let r = diff.diff.diffLines(\"foo\\nbar\\nqux\\n\", \"foo\\nbaz\\nqux\\n\", null); let result = r.hunks[0].oldStart;");
        long newStart = RunDiffInt("let r = diff.diff.diffLines(\"foo\\nbar\\nqux\\n\", \"foo\\nbaz\\nqux\\n\", null); let result = r.hunks[0].newStart;");
        Assert.Equal(1L, oldStart);
        Assert.Equal(1L, newStart);
    }

    [Fact]
    public void Edit_DeleteOp_HasNullNewLine()
    {
        // The Hunk's edits include EQUAL context, then DELETE, then INSERT.
        // Find the DELETE edit (text == "bar") and assert newLine is null.
        string body = @"
let r = diff.diff.diffLines(""foo\nbar\nqux\n"", ""foo\nbaz\nqux\n"", null);
let edits = r.hunks[0].edits;
let result = null;
for (let e in edits) {
  if (e.op == diff.types.Op.DELETE) {
    result = e.newLine == null;
  }
}";
        Assert.True(RunDiffBool(body));
    }

    [Fact]
    public void Edit_InsertOp_HasNullOldLine()
    {
        string body = @"
let r = diff.diff.diffLines(""foo\nbar\nqux\n"", ""foo\nbaz\nqux\n"", null);
let edits = r.hunks[0].edits;
let result = null;
for (let e in edits) {
  if (e.op == diff.types.Op.INSERT) {
    result = e.oldLine == null;
  }
}";
        Assert.True(RunDiffBool(body));
    }

    // ── 13. Larger smoke test ────────────────────────────────────────────────

    [Fact]
    public void DiffLines_LargeIdenticalInput_Fast()
    {
        // 200 identical lines on each side completes near-instantly and
        // returns equal=true. (Not the 5000-line bench from the spec — kept
        // to a size that runs comfortably under CI test parallelism.)
        string body = @"
let a = """";
let i = 0;
while (i < 200) {
  a = a + ""line "" + conv.toStr(i) + ""\n"";
  i = i + 1;
}
let r = diff.diff.diffLines(a, a, null);
let result = r.equal;";
        Assert.True(RunDiffBool(body));
    }
}
