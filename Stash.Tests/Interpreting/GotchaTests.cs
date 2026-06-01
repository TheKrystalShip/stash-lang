namespace Stash.Tests.Interpreting;

/// <summary>
/// Gotcha tests are <b>change-detectors</b>, not normal assertions. Each one
/// asserts the CURRENT buggy/quirky behavior of a known Stash bug, so the test
/// is GREEN today and flips RED the moment the bug is fixed.
///
/// A red Gotcha test is the signal — NOT a regression to "fix" by editing the
/// test back to green. When one fails:
///   1. Confirm the underlying bug is actually fixed.
///   2. Flip the assertion to the now-correct behavior (it graduates into a
///      permanent regression guard).
///   3. Prune the matching entry in .claude/agents/stash-author.gotchas.md and
///      resolve the linked backlog stub.
///
/// NEVER add Category=Gotcha to a final_verify / CI exclusion filter — unlike
/// documented flakies, the red is the entire point. This category is maintained
/// by the stash-author agent (see .claude/agents/stash-author.md).
/// </summary>
[Trait("Category", "Gotcha")]
public class GotchaTests : TempDirectoryFixture
{
    public GotchaTests() : base("stash_gotcha_test") { }

    /// <summary>
    /// GOTCHA: fs.move / fs.copy are file-only — there is no directory-capable
    /// variant in the fs namespace, so moving a directory throws.
    ///   backlog: .kanban/0-backlog/stdlib/fs-move-copy-file-only.md
    ///   memory:  .claude/agents/stash-author.gotchas.md  (id: fs-move-copy-file-only)
    /// WHEN THIS GOES RED: fs.move learned to move directories (or fs.moveDir was
    /// added). Flip this to assert the move succeeded, then prune the gotcha.
    /// </summary>
    [Fact]
    public void FsMove_Directory_CurrentlyThrows()
    {
        var src = Path.Combine(TestDir, "srcdir").Replace("\\", "\\\\");
        var dst = Path.Combine(TestDir, "dstdir").Replace("\\", "\\\\");
        Directory.CreateDirectory(Path.Combine(TestDir, "srcdir"));

        // fs.move is documented as moving a *file*; on a directory it throws today.
        RunExpectingError($"fs.move(\"{src}\", \"{dst}\");");
    }
}
