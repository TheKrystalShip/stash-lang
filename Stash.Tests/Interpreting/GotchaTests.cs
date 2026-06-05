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

    /// <summary>
    /// GOTCHA: process.read is documented as "Non-blocking read — returns a string
    /// chunk if data is available, or null if no data is ready." In practice, when
    /// the child's stdout pipe buffer is empty (no pending data), process.read BLOCKS
    /// indefinitely instead of returning null immediately.
    ///
    /// Repro: spawn a child that outputs one line then sleeps; after reading the first
    /// line, poll process.read — it hangs instead of returning null.
    ///
    /// The test detects this by running the read inside a task.run with a known
    /// sleep + read sequence: if the read after the child quiets returns quickly (null),
    /// the gotcha is fixed. Currently the task never completes within the test timeout
    /// unless we add our own artificial sleep BEFORE the process.read to avoid the race.
    ///
    ///   backlog: .kanban/0-backlog/stdlib/process-read-blocks-on-empty-pipe.md
    ///   memory:  .claude/agents/stash-author.gotchas.md  (id: process-read-blocks-empty-pipe)
    /// WHEN THIS GOES RED: process.read returns null immediately on an empty pipe.
    /// Flip: assert result is "null", then prune the gotcha.
    /// </summary>
    [Fact]
    public void ProcessRead_EmptyPipe_CurrentlyBlocks()
    {
        // Spawn a child that writes one line then goes silent for 30s.
        // We read (drain) the first line, then attempt a second process.read.
        // Because process.read blocks on an empty pipe, the second read hangs —
        // we wrap the whole attempt in a task with a 3s timeout to detect this.
        // GREEN (buggy): the task times out → result is null (timeout, not a read result).
        // RED (fixed):   the task completes quickly → result is "null" (the null return value).
        // Run the probe as an external subprocess with a hard wall-clock timeout.
        // If process.read is non-blocking (fixed), the probe completes in <1s and prints "fast".
        // If process.read blocks (current buggy behavior), the probe hangs until bash exits (~30s).
        // We give it 5s — if it completes inside 5s, it printed "fast" (bug fixed).
        // If it times out (still running at 5s), the bug is present.
        var probe = @"
let p = process.spawn(""bash -c 'echo READY; sleep 30'"");
let deadline = time.now() + 2.0;
while (time.now() < deadline) {
    let c = process.read(p);
    if (c != null && str.contains(c, ""READY"")) { break; }
    if (c == null) { time.sleep(0.05); }
}
let t0 = time.now();
let r = process.read(p);
let elapsed = time.now() - t0;
process.kill(p);
io.println(elapsed < 0.5 ? ""fast"" : ""slow"");
";
        var tmpFile = Path.Combine(Path.GetTempPath(), "process_read_gotcha_test.stash");
        File.WriteAllText(tmpFile, probe);

        var stashExe = typeof(StashTestBase).Assembly.Location;
        // Find the Stash.Cli binary path from assembly location
        var stashCli = Path.Combine(
            AppContext.BaseDirectory.Replace("Stash.Tests", "Stash.Cli").Split("bin")[0],
            "bin", "Debug", "net10.0", "Stash");

        bool processCompleted;
        using var proc = new System.Diagnostics.Process();
        proc.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = stashCli,
            Arguments = tmpFile,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        proc.Start();
        processCompleted = proc.WaitForExit(5000); // 5s timeout
        if (!processCompleted)
            proc.Kill(entireProcessTree: true);

        // Currently buggy: process.read blocks, process does NOT complete in 5s.
        // When fixed: process completes quickly (proc.StandardOutput.ReadToEnd() == "fast\n").
        Assert.False(processCompleted, "Expected process.read to block (bug present). It completed — bug may be fixed. Flip this to assert output == 'fast'.");
    }
}
