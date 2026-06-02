using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class FsWatchBuiltInsTests : TempDirectoryFixture
{
    public FsWatchBuiltInsTests() : base("stash_fswatch_test") { }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void Watch_FileModified_CallbackFires()
    {
        // Callback fires with Modified event type — prove via file side-effect channel.
        var filePath   = Path.Combine(TestDir, "watch_modified.txt").Replace("\\", "/");
        var signalFile = Path.Combine(TestDir, "watch_modified_signal.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        // Callback writes the event type string to signalFile; parent polls until it appears.
        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {
                if (event.type == fs.WatchEventType.Modified) {
                    fs.writeFile("{{signalFile}}", "Modified");
                }
            });
            fs.writeFile("{{filePath}}", "modified content");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        Assert.True(File.Exists(signalFile), "callback did not fire: signal file not written within 5s");
        Assert.Equal("Modified", File.ReadAllText(signalFile));
    }

    [Fact]
    public void Watch_DirectoryFileCreated_CallbackFires()
    {
        var dir        = Path.Combine(TestDir, "watch_created_dir").Replace("\\", "/");
        var newFile    = $"{dir}/newfile.txt";
        var signalFile = Path.Combine(TestDir, "watch_created_signal.txt").Replace("\\", "/");
        Directory.CreateDirectory(dir);

        // Poll for the Created callback up to a 5s deadline instead of a fixed sleep.
        // Callback writes to signalFile when a Created event fires; parent polls that file.
        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                if (event.type == fs.WatchEventType.Created) {
                    fs.writeFile("{{signalFile}}", "Created");
                }
            });
            fs.writeFile("{{newFile}}", "hello");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        Assert.True(File.Exists(signalFile), "callback did not fire: signal file not written within 5s");
        Assert.Equal("Created", File.ReadAllText(signalFile));
    }

    [Fact]
    public void Watch_DirectoryFileDeleted_CallbackFires()
    {
        var dir        = Path.Combine(TestDir, "watch_deleted_dir").Replace("\\", "/");
        var target     = $"{dir}/todelete.txt";
        var signalFile = Path.Combine(TestDir, "watch_deleted_signal.txt").Replace("\\", "/");
        Directory.CreateDirectory(dir);
        File.WriteAllText(target, "data");

        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                if (event.type == fs.WatchEventType.Deleted) {
                    fs.writeFile("{{signalFile}}", "Deleted");
                }
            });
            fs.delete("{{target}}");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        Assert.True(File.Exists(signalFile), "callback did not fire: signal file not written within 5s");
        Assert.Equal("Deleted", File.ReadAllText(signalFile));
    }

    [Fact]
    public void Watch_DirectoryFileRenamed_CallbackFires()
    {
        var dir         = Path.Combine(TestDir, "watch_renamed_dir").Replace("\\", "/");
        var oldFile     = $"{dir}/before.txt";
        var newFile     = $"{dir}/after.txt";
        var signalFile  = Path.Combine(TestDir, "watch_renamed_signal.txt").Replace("\\", "/");
        var oldPathFile = Path.Combine(TestDir, "watch_renamed_oldpath.txt").Replace("\\", "/");
        Directory.CreateDirectory(dir);
        File.WriteAllText(oldFile, "data");

        // First: confirm Renamed event type fires.
        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                if (event.type == fs.WatchEventType.Renamed) {
                    fs.writeFile("{{signalFile}}", "Renamed");
                }
            });
            fs.move("{{oldFile}}", "{{newFile}}");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        Assert.True(File.Exists(signalFile), "Renamed callback did not fire: signal file not written within 5s");
        Assert.Equal("Renamed", File.ReadAllText(signalFile));

        // Second: confirm oldPath is non-null on a rename event.
        File.WriteAllText(oldFile, "data");
        if (File.Exists(newFile)) File.Delete(newFile);
        if (File.Exists(oldPathFile)) File.Delete(oldPathFile);

        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                if (event.oldPath != null) {
                    fs.writeFile("{{oldPathFile}}", "has-old-path");
                }
            });
            fs.move("{{oldFile}}", "{{newFile}}");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{oldPathFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        Assert.True(File.Exists(oldPathFile), "oldPath callback did not fire: old-path file not written within 5s");
        Assert.Equal("has-old-path", File.ReadAllText(oldPathFile));
    }

    [Fact]
    public void Watch_WithFilter_OnlyMatchingFilesTrigger()
    {
        var dir        = Path.Combine(TestDir, "watch_filter_dir").Replace("\\", "/");
        var match      = $"{dir}/match.txt";
        var noMatch    = $"{dir}/nomatch.log";
        // Counter file: each callback invocation appends a line.
        var counterFile = Path.Combine(TestDir, "watch_filter_count.txt").Replace("\\", "/");
        // Pre-create both files so only "modified" events fire.
        Directory.CreateDirectory(dir);
        File.WriteAllText(match, "initial");
        File.WriteAllText(noMatch, "initial");

        // Callback appends a line to counterFile for every event; we count lines.
        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                fs.appendFile("{{counterFile}}", "hit\n");
            }, fs.WatchOptions { filter: "*.txt" });
            fs.writeFile("{{match}}", "updated");
            fs.writeFile("{{noMatch}}", "updated");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{counterFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            time.sleep(0.5);
            fs.unwatch(w);
            """);

        // Exactly 1 callback for the .txt file (debounce coalesces the single write).
        int count = File.Exists(counterFile)
            ? File.ReadAllLines(counterFile).Count(l => l == "hit")
            : 0;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Watch_Recursive_SubdirChangesDetected()
    {
        var dir        = Path.Combine(TestDir, "watch_recursive_dir").Replace("\\", "/");
        var subdir     = $"{dir}/sub";
        var deepFile   = $"{subdir}/deep.txt";
        var signalFile = Path.Combine(TestDir, "watch_recursive_signal.txt").Replace("\\", "/");
        Directory.CreateDirectory(subdir);

        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                fs.writeFile("{{signalFile}}", "fired");
            }, fs.WatchOptions { recursive: true });
            fs.writeFile("{{deepFile}}", "hello");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        Assert.True(File.Exists(signalFile), "recursive callback did not fire within 5s");
    }

    [Fact]
    public void Unwatch_StopsCallbacks()
    {
        // After unwatch, no callback fires — the counter file should remain absent.
        var filePath    = Path.Combine(TestDir, "watch_stopped.txt").Replace("\\", "/");
        var counterFile = Path.Combine(TestDir, "watch_stopped_count.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {
                fs.writeFile("{{counterFile}}", "fired");
            });
            fs.unwatch(w);
            fs.writeFile("{{filePath}}", "after unwatch");
            time.sleep(0.8);
            """);

        // Counter file should NOT exist — unwatch stopped the callbacks.
        Assert.False(File.Exists(counterFile), "callback fired after unwatch");
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Watch_NonExistentPath_ThrowsError()
    {
        var missing = Path.Combine(TestDir, "does_not_exist").Replace("\\", "/");
        RunExpectingError($$"""fs.watch("{{missing}}", (event) => {});""");
    }

    [Fact]
    public void Unwatch_InvalidHandle_NoOp()
    {
        var filePath = Path.Combine(TestDir, "unwatch_invalid.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "data");

        // Watcher handle becomes invalid (inactive) after unwatch. Re-calling must be a no-op.
        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {});
            fs.unwatch(w);
            fs.unwatch(w);
            """);
    }

    [Fact]
    public void Unwatch_Double_NoOp()
    {
        var filePath = Path.Combine(TestDir, "unwatch_double.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "data");

        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {});
            fs.unwatch(w);
            fs.unwatch(w);
            """);
    }

    [Fact]
    public void Watch_CallbackError_WatcherContinues()
    {
        // The watcher continues firing after a callback throws.
        // First invocation: throw an error (no signal file written).
        // Second invocation: write to signalFile.
        // The parent waits for signalFile to appear — proving the watcher survived the first error.
        var filePath   = Path.Combine(TestDir, "watch_cb_error.txt").Replace("\\", "/");
        var errorFile  = Path.Combine(TestDir, "watch_cb_error_first.txt").Replace("\\", "/");
        var signalFile = Path.Combine(TestDir, "watch_cb_error_signal.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        // The callback appends a line to errorFile on each invocation (before the throw on call 1),
        // and writes to signalFile on the second (non-throwing) invocation.
        // We can't share a counter via upvalue (isolation), so we track "first time" by whether
        // errorFile already exists: if missing → first call → write errorFile then throw;
        //                                         if present → second call → write signalFile.
        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {
                if (!fs.exists("{{errorFile}}")) {
                    fs.writeFile("{{errorFile}}", "first");
                    throw "intentional error";
                }
                fs.writeFile("{{signalFile}}", "second");
            });
            fs.writeFile("{{filePath}}", "first");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{errorFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.writeFile("{{filePath}}", "second");
            let deadline2 = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline2) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        Assert.True(File.Exists(signalFile),
            "watcher did not continue after callback error: second signal file was not written within 5s");
    }

    [Fact]
    public void Watch_SamePathTwice_BothFire()
    {
        // Two watchers on the same path — both callbacks fire at least once.
        var filePath    = Path.Combine(TestDir, "watch_two.txt").Replace("\\", "/");
        var signalFile1 = Path.Combine(TestDir, "watch_two_signal1.txt").Replace("\\", "/");
        var signalFile2 = Path.Combine(TestDir, "watch_two_signal2.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        RunStatements($$"""
            let w1 = fs.watch("{{filePath}}", (event) => {
                fs.writeFile("{{signalFile1}}", "w1");
            });
            let w2 = fs.watch("{{filePath}}", (event) => {
                fs.writeFile("{{signalFile2}}", "w2");
            });
            fs.writeFile("{{filePath}}", "changed");
            let deadline = time.millis() + 5000;
            while ((!fs.exists("{{signalFile1}}") || !fs.exists("{{signalFile2}}")) && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w1);
            fs.unwatch(w2);
            """);

        Assert.True(File.Exists(signalFile1), "watcher 1 did not fire within 5s");
        Assert.True(File.Exists(signalFile2), "watcher 2 did not fire within 5s");
    }

    [Fact]
    public void Watch_MultipleWatchersDifferentPaths_IndependentFiring()
    {
        var dir1        = Path.Combine(TestDir, "multi_watch_a").Replace("\\", "/");
        var dir2        = Path.Combine(TestDir, "multi_watch_b").Replace("\\", "/");
        var signalFileA = Path.Combine(TestDir, "multi_watch_signal_a.txt").Replace("\\", "/");
        var signalFileB = Path.Combine(TestDir, "multi_watch_signal_b.txt").Replace("\\", "/");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        RunStatements($$"""
            let w1 = fs.watch("{{dir1}}", (event) => {
                fs.writeFile("{{signalFileA}}", "fired");
            });
            let w2 = fs.watch("{{dir2}}", (event) => {
                fs.writeFile("{{signalFileB}}", "fired");
            });
            fs.writeFile("{{dir1}}/file.txt", "hello");
            fs.writeFile("{{dir2}}/file.txt", "world");
            let deadline = time.millis() + 5000;
            while ((!fs.exists("{{signalFileA}}") || !fs.exists("{{signalFileB}}")) && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w1);
            fs.unwatch(w2);
            """);

        Assert.True(File.Exists(signalFileA), "watcher for dir1 did not fire within 5s");
        Assert.True(File.Exists(signalFileB), "watcher for dir2 did not fire within 5s");
    }

    // ── Options tests ────────────────────────────────────────────────────────

    [Fact]
    public void Watch_DefaultOptions_Works()
    {
        var filePath   = Path.Combine(TestDir, "watch_default.txt").Replace("\\", "/");
        var signalFile = Path.Combine(TestDir, "watch_default_signal.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {
                if (event.type == fs.WatchEventType.Modified) {
                    fs.writeFile("{{signalFile}}", "Modified");
                }
            });
            fs.writeFile("{{filePath}}", "updated");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        Assert.True(File.Exists(signalFile), "default-options callback did not fire within 5s");
        Assert.Equal("Modified", File.ReadAllText(signalFile));
    }

    [Fact]
    public void Watch_RecursiveFalse_SubdirIgnored()
    {
        var dir         = Path.Combine(TestDir, "watch_nonrecursive").Replace("\\", "/");
        var subdir      = $"{dir}/sub";
        var ignoredFile = $"{subdir}/ignored.txt";
        var counterFile = Path.Combine(TestDir, "watch_nonrecursive_count.txt").Replace("\\", "/");
        Directory.CreateDirectory(subdir);

        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                fs.writeFile("{{counterFile}}", "fired");
            }, fs.WatchOptions { recursive: false });
            fs.writeFile("{{ignoredFile}}", "hello");
            time.sleep(0.8);
            fs.unwatch(w);
            """);

        // Counter file should NOT exist — subdir change was not watched.
        Assert.False(File.Exists(counterFile), "recursive:false should not fire for subdirectory changes");
    }

    [Fact]
    public void Watch_CustomBufferSize_AcceptedWithoutError()
    {
        var filePath   = Path.Combine(TestDir, "watch_bufsize.txt").Replace("\\", "/");
        var signalFile = Path.Combine(TestDir, "watch_bufsize_signal.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {
                fs.writeFile("{{signalFile}}", "fired");
            }, fs.WatchOptions { bufferSize: 16384 });
            fs.writeFile("{{filePath}}", "updated");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        Assert.True(File.Exists(signalFile), "bufferSize callback did not fire within 5s");
    }

    // ── Debounce tests ───────────────────────────────────────────────────────

    [Fact]
    public void Watch_DebouncedRapidWrites_SingleCallback()
    {
        // 5 rapid writes to the same file → debounced to exactly 1 callback.
        // Callback appends a line to counterFile per invocation.
        var filePath    = Path.Combine(TestDir, "watch_debounce.txt").Replace("\\", "/");
        var counterFile = Path.Combine(TestDir, "watch_debounce_count.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {
                fs.appendFile("{{counterFile}}", "hit\n");
            });
            fs.writeFile("{{filePath}}", "1");
            fs.writeFile("{{filePath}}", "2");
            fs.writeFile("{{filePath}}", "3");
            fs.writeFile("{{filePath}}", "4");
            fs.writeFile("{{filePath}}", "5");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{counterFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            time.sleep(0.5);
            fs.unwatch(w);
            """);

        int count = File.Exists(counterFile)
            ? File.ReadAllLines(counterFile).Count(l => l == "hit")
            : 0;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Watch_DebounceZero_AllEventsRaw()
    {
        // debounce:0 fires on every raw event — at least 2 callbacks for 5 distinct file creates.
        var subDir      = Path.Combine(TestDir, "watch_nodebounce").Replace("\\", "/");
        var counterFile = Path.Combine(TestDir, "watch_nodebounce_count.txt").Replace("\\", "/");
        Directory.CreateDirectory(subDir);

        RunStatements($$"""
            let w = fs.watch("{{subDir}}", (event) => {
                fs.appendFile("{{counterFile}}", "hit\n");
            }, fs.WatchOptions { debounce: 0 });
            fs.writeFile("{{subDir}}/a.txt", "1");
            fs.writeFile("{{subDir}}/b.txt", "2");
            fs.writeFile("{{subDir}}/c.txt", "3");
            fs.writeFile("{{subDir}}/d.txt", "4");
            fs.writeFile("{{subDir}}/e.txt", "5");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{counterFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            time.sleep(1.0);
            fs.unwatch(w);
            """);

        int count = File.Exists(counterFile)
            ? File.ReadAllLines(counterFile).Count(l => l == "hit")
            : 0;
        Assert.True(count > 1, $"debounce:0 should fire >1 callbacks for 5 distinct creates, got {count}");
    }

    [Fact]
    public void Watch_DebounceDifferentFiles_NoCoalescing()
    {
        // The debounce timer is keyed on "{path}:{eventType}" (FsBuiltIns.FireCallback),
        // so three writes to three DISTINCT files land in three distinct debounce buckets
        // and never coalesce — each is guaranteed its own callback. >= 3 is therefore the
        // exact contract guarantee. We poll until all 3 callback lines appear (up to 5s deadline).
        var dir         = Path.Combine(TestDir, "debounce_diff").Replace("\\", "/");
        var counterFile = Path.Combine(TestDir, "debounce_diff_count.txt").Replace("\\", "/");
        Directory.CreateDirectory(dir);

        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                fs.appendFile("{{counterFile}}", "hit\n");
            });
            fs.writeFile("{{dir}}/a.txt", "1");
            fs.writeFile("{{dir}}/b.txt", "2");
            fs.writeFile("{{dir}}/c.txt", "3");
            let deadline = time.millis() + 5000;
            while (time.millis() < deadline) {
                if (fs.exists("{{counterFile}}")) {
                    let lines = str.split(fs.readFile("{{counterFile}}"), "\n");
                    let count = 0;
                    let i = 0;
                    while (i < len(lines)) {
                        if (lines[i] == "hit") { count = count + 1; }
                        i = i + 1;
                    }
                    if (count >= 3) { break; }
                }
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        int count = File.Exists(counterFile)
            ? File.ReadAllLines(counterFile).Count(l => l == "hit")
            : 0;
        Assert.True(count >= 3, $"expected >= 3 callbacks for 3 distinct-file writes, got {count}");
    }

    [Fact]
    public void Watch_RenameNotDebounced_EachFiresImmediately()
    {
        // Each rename fires its own Renamed callback immediately (not debounced together).
        var dir         = Path.Combine(TestDir, "debounce_rename").Replace("\\", "/");
        var counterFile = Path.Combine(TestDir, "debounce_rename_count.txt").Replace("\\", "/");
        Directory.CreateDirectory(dir);
        File.WriteAllText($"{dir}/r1.txt", "data");
        File.WriteAllText($"{dir}/r2.txt", "data");

        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                if (event.type == fs.WatchEventType.Renamed) {
                    fs.appendFile("{{counterFile}}", "rename\n");
                }
            });
            fs.move("{{dir}}/r1.txt", "{{dir}}/r1_moved.txt");
            fs.move("{{dir}}/r2.txt", "{{dir}}/r2_moved.txt");
            let deadline = time.millis() + 5000;
            while (time.millis() < deadline) {
                if (fs.exists("{{counterFile}}")) {
                    let lines = str.split(fs.readFile("{{counterFile}}"), "\n");
                    let count = 0;
                    let i = 0;
                    while (i < len(lines)) {
                        if (lines[i] == "rename") { count = count + 1; }
                        i = i + 1;
                    }
                    if (count >= 2) { break; }
                }
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        int count = File.Exists(counterFile)
            ? File.ReadAllLines(counterFile).Count(l => l == "rename")
            : 0;
        Assert.True(count >= 2, $"expected >= 2 rename callbacks, got {count}");
    }

    [Fact]
    public void Watch_CustomDebounceWindow_CoalescesWithinWindow()
    {
        // 3 rapid writes within the debounce window coalesce to 1 callback.
        var filePath    = Path.Combine(TestDir, "custom_debounce.txt").Replace("\\", "/");
        var counterFile = Path.Combine(TestDir, "custom_debounce_count.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {
                fs.appendFile("{{counterFile}}", "hit\n");
            }, fs.WatchOptions { debounce: 500 });
            fs.writeFile("{{filePath}}", "1");
            fs.writeFile("{{filePath}}", "2");
            fs.writeFile("{{filePath}}", "3");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{counterFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            time.sleep(0.5);
            fs.unwatch(w);
            """);

        int count = File.Exists(counterFile)
            ? File.ReadAllLines(counterFile).Count(l => l == "hit")
            : 0;
        Assert.Equal(1, count);
    }

    // ── Scope isolation tests ─────────────────────────────────────────────────

    [Fact]
    public void Watch_Callback_FiresOnChange_MutationIsCallLocal()
    {
        // Background-thread callbacks (fs.watch) run in an isolated child VM.
        // They CANNOT mutate outer/parent globals — primitive rebinds (x = 99) are
        // call-local: the child has its own copy of the global slot, and the parent
        // never sees the write.  The callback can communicate via I/O side effects.
        var watchedFile = Path.Combine(TestDir, "watch_fork.txt").Replace("\\", "/");
        var signalFile  = Path.Combine(TestDir, "watch_fork_signal.txt").Replace("\\", "/");
        File.WriteAllText(watchedFile, "initial");

        // (a) Prove the callback fired via a side-effect channel: the callback writes
        //     to signalFile.  The parent polls until the file appears (up to 5s deadline)
        //     so slow FSW latency is tolerated without a fixed sleep.
        // (b) x remains 0 in the parent — documents call-local primitive mutation.
        var result = Run($$"""
            let x = 0;
            let w = fs.watch("{{watchedFile}}", (event) => {
                x = 99;
                fs.writeFile("{{signalFile}}", "fired");
            });
            fs.writeFile("{{watchedFile}}", "trigger");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            let result = x;
            """);

        // (a) Callback fired — signal file was written by the child VM.
        Assert.True(File.Exists(signalFile), "callback did not fire: signal file was not written within 5s");
        Assert.Equal("fired", File.ReadAllText(signalFile));

        // (b) Parent's global x is still 0 — primitive rebind inside the background-thread
        //     callback is call-local and never propagates to the parent VM.
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Watch_UpvalueRefType_DictMutationIsCallLocal()
    {
        // Background-thread callbacks (fs.watch) run in an isolated child VM whose
        // upvalues are snapshotted via IsolationHelpers.SnapshotUpvalues.  A mutation
        // of a reference-typed captured local (dict) inside the callback is call-local:
        // the child gets its own deep-cloned copy of the dict, so the parent's dict is
        // unchanged after the callback fires.
        //
        // Prove the callback fired via a side-effect channel (signalFile write), then
        // assert the parent's captured dict is unchanged — mirroring the primitive test
        // Watch_Callback_FiresOnChange_MutationIsCallLocal.
        var filePath   = Path.Combine(TestDir, "watch_upvalue_ref.txt").Replace("\\", "/");
        var signalFile = Path.Combine(TestDir, "watch_upvalue_ref_signal.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        var result = Run($$"""
            let state = {value: 0};
            let w = fs.watch("{{filePath}}", (event) => {
                state.value = 42;
                fs.writeFile("{{signalFile}}", "fired");
            });
            fs.writeFile("{{filePath}}", "trigger");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            fs.unwatch(w);
            let result = state.value;
            """);

        // Callback fired — signal file was written by the child VM.
        Assert.True(File.Exists(signalFile), "callback did not fire: signal file was not written within 5s");
        Assert.Equal("fired", File.ReadAllText(signalFile));

        // Parent's captured dict is unchanged — upvalue snapshot makes mutation call-local.
        Assert.Equal(0L, result);
    }
}
