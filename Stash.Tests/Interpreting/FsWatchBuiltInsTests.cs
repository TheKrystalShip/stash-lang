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
        // .txt write triggers callback; .log write does not (filter excludes it).
        // Use a single signal file — its presence proves the .txt callback fired.
        var dir        = Path.Combine(TestDir, "watch_filter_dir").Replace("\\", "/");
        var match      = $"{dir}/match.txt";
        var noMatch    = $"{dir}/nomatch.log";
        var signalFile = Path.Combine(TestDir, "watch_filter_signal.txt").Replace("\\", "/");
        // Second signal file that would only appear if the .log callback fired.
        var logSignal  = Path.Combine(TestDir, "watch_filter_log_signal.txt").Replace("\\", "/");
        Directory.CreateDirectory(dir);
        File.WriteAllText(match, "initial");
        File.WriteAllText(noMatch, "initial");

        // Callback writes signalFile for any event (should only fire for .txt due to filter).
        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                fs.writeFile("{{signalFile}}", event.path);
            }, fs.WatchOptions { filter: "*.txt" });
            fs.writeFile("{{match}}", "updated");
            fs.writeFile("{{noMatch}}", "updated");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{signalFile}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            time.sleep(0.5);
            fs.unwatch(w);
            """);

        // Signal file exists → .txt callback fired.
        Assert.True(File.Exists(signalFile), "no callback fired for .txt write");
        // The path in signalFile must contain "match.txt" (the filtered-in file).
        string signalContent = File.ReadAllText(signalFile);
        Assert.Contains("match.txt", signalContent);
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
        // 5 rapid writes to the SAME file → debounced to exactly 1 callback.
        // Each callback invocation writes a unique file whose name contains time.millis().
        // After the first signal appears we wait a debounce period, then count signal files in C#.
        var filePath  = Path.Combine(TestDir, "watch_debounce.txt").Replace("\\", "/");
        var signalDir = Path.Combine(TestDir, "watch_debounce_signals").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");
        Directory.CreateDirectory(signalDir);

        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {
                fs.writeFile("{{signalDir}}/" + conv.toStr(time.millis()) + ".txt", "fired");
            });
            fs.writeFile("{{filePath}}", "1");
            fs.writeFile("{{filePath}}", "2");
            fs.writeFile("{{filePath}}", "3");
            fs.writeFile("{{filePath}}", "4");
            fs.writeFile("{{filePath}}", "5");
            let deadline = time.millis() + 5000;
            while (len(fs.listDir("{{signalDir}}")) == 0 && time.millis() < deadline) {
                time.sleep(0.02);
            }
            time.sleep(0.5);
            fs.unwatch(w);
            """);

        int count = Directory.Exists(signalDir) ? Directory.GetFiles(signalDir).Length : 0;
        Assert.True(count >= 1, "debounce: no callback fired within 5s");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Watch_DebounceZero_AllEventsRaw()
    {
        // debounce:0 fires on every raw event — create 5 distinct files → at least 2 callbacks.
        // Each callback creates a unique signal file named after the event path (base name only).
        // C# counts the signal files after waiting.
        var subDir      = Path.Combine(TestDir, "watch_nodebounce").Replace("\\", "/");
        var signalDir   = Path.Combine(TestDir, "watch_nodebounce_signals").Replace("\\", "/");
        var firstSignal = Path.Combine(signalDir, "a.txt").Replace("\\", "/");
        Directory.CreateDirectory(subDir);
        Directory.CreateDirectory(signalDir);

        RunStatements($$"""
            let w = fs.watch("{{subDir}}", (event) => {
                let parts = str.split(event.path, "/");
                let name = parts[len(parts) - 1];
                fs.writeFile("{{signalDir}}/" + name, "fired");
            }, fs.WatchOptions { debounce: 0 });
            fs.writeFile("{{subDir}}/a.txt", "1");
            fs.writeFile("{{subDir}}/b.txt", "2");
            fs.writeFile("{{subDir}}/c.txt", "3");
            fs.writeFile("{{subDir}}/d.txt", "4");
            fs.writeFile("{{subDir}}/e.txt", "5");
            let deadline = time.millis() + 5000;
            while (!fs.exists("{{firstSignal}}") && time.millis() < deadline) {
                time.sleep(0.02);
            }
            time.sleep(1.0);
            fs.unwatch(w);
            """);

        int count = Directory.Exists(signalDir) ? Directory.GetFiles(signalDir).Length : 0;
        Assert.True(count > 1, $"debounce:0 should fire >1 callbacks for 5 distinct creates, got {count}");
    }

    [Fact]
    public void Watch_DebounceDifferentFiles_NoCoalescing()
    {
        // The debounce timer is keyed on "{path}:{eventType}" (FsBuiltIns.FireCallback),
        // so three writes to three DISTINCT files land in three distinct debounce buckets
        // and never coalesce — each is guaranteed its own callback. >= 3 is therefore the
        // exact contract guarantee.
        //
        // Each callback creates a unique signal file named after the event path.
        // Stash polls until all 3 signal files appear (up to 5s deadline).
        var dir       = Path.Combine(TestDir, "debounce_diff").Replace("\\", "/");
        var signalDir = Path.Combine(TestDir, "debounce_diff_signals").Replace("\\", "/");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(signalDir);

        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                let parts = str.split(event.path, "/");
                let name = parts[len(parts) - 1];
                fs.writeFile("{{signalDir}}/" + name, "fired");
            });
            fs.writeFile("{{dir}}/a.txt", "1");
            fs.writeFile("{{dir}}/b.txt", "2");
            fs.writeFile("{{dir}}/c.txt", "3");
            let deadline = time.millis() + 5000;
            while (time.millis() < deadline) {
                if (fs.exists("{{signalDir}}/a.txt") &&
                    fs.exists("{{signalDir}}/b.txt") &&
                    fs.exists("{{signalDir}}/c.txt")) {
                    break;
                }
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        int count = Directory.Exists(signalDir) ? Directory.GetFiles(signalDir).Length : 0;
        Assert.True(count >= 3, $"expected >= 3 callbacks for 3 distinct-file writes, got {count}");
    }

    [Fact]
    public void Watch_RenameNotDebounced_EachFiresImmediately()
    {
        // Each rename fires its own Renamed callback immediately (not debounced together).
        // 2 renames → 2 distinct signal files (named after the destination path).
        var dir       = Path.Combine(TestDir, "debounce_rename").Replace("\\", "/");
        var signalDir = Path.Combine(TestDir, "debounce_rename_signals").Replace("\\", "/");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(signalDir);
        File.WriteAllText($"{dir}/r1.txt", "data");
        File.WriteAllText($"{dir}/r2.txt", "data");

        RunStatements($$"""
            let w = fs.watch("{{dir}}", (event) => {
                if (event.type == fs.WatchEventType.Renamed) {
                    let parts = str.split(event.path, "/");
                    let name = parts[len(parts) - 1];
                    fs.writeFile("{{signalDir}}/" + name, "fired");
                }
            });
            fs.move("{{dir}}/r1.txt", "{{dir}}/r1_moved.txt");
            fs.move("{{dir}}/r2.txt", "{{dir}}/r2_moved.txt");
            let deadline = time.millis() + 5000;
            while (time.millis() < deadline) {
                if (fs.exists("{{signalDir}}/r1_moved.txt") &&
                    fs.exists("{{signalDir}}/r2_moved.txt")) {
                    break;
                }
                time.sleep(0.02);
            }
            fs.unwatch(w);
            """);

        int count = Directory.Exists(signalDir) ? Directory.GetFiles(signalDir).Length : 0;
        Assert.True(count >= 2, $"expected >= 2 rename callbacks, got {count}");
    }

    [Fact]
    public void Watch_CustomDebounceWindow_CoalescesWithinWindow()
    {
        // 3 rapid writes to the SAME file within the debounce window coalesce to 1 callback.
        // Each callback invocation writes a unique file whose name contains time.millis().
        // After the first signal appears we wait a debounce period, then count signal files in C#.
        var filePath  = Path.Combine(TestDir, "custom_debounce.txt").Replace("\\", "/");
        var signalDir = Path.Combine(TestDir, "custom_debounce_signals").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");
        Directory.CreateDirectory(signalDir);

        RunStatements($$"""
            let w = fs.watch("{{filePath}}", (event) => {
                fs.writeFile("{{signalDir}}/" + conv.toStr(time.millis()) + ".txt", "fired");
            }, fs.WatchOptions { debounce: 500 });
            fs.writeFile("{{filePath}}", "1");
            fs.writeFile("{{filePath}}", "2");
            fs.writeFile("{{filePath}}", "3");
            let deadline = time.millis() + 5000;
            while (len(fs.listDir("{{signalDir}}")) == 0 && time.millis() < deadline) {
                time.sleep(0.02);
            }
            time.sleep(0.5);
            fs.unwatch(w);
            """);

        int count = Directory.Exists(signalDir) ? Directory.GetFiles(signalDir).Length : 0;
        Assert.True(count >= 1, "custom debounce: no callback fired within 5s");
        Assert.Equal(1, count);
    }

    // ── Queued-delivery mutation tests ───────────────────────────────────────
    //
    // Background-thread callbacks (fs.watch) are now marshaled onto the VM thread via
    // a per-VM callback queue.  Delivery happens at the next drain point (time.sleep).
    // This gives Branch-1 (shared) semantics: mutations inside the callback ARE visible
    // to the parent after time.sleep returns.  These tests document the new behavior.

    [Fact]
    public void Watch_Callback_FiresOnChange_MutationIsSharedViaQueuedDelivery()
    {
        // Background-thread fs.watch callbacks are enqueued and delivered on the VM thread
        // at the next time.sleep drain point.  A primitive rebind (x = 99) inside the
        // callback IS visible to the parent after the sleep that drained it returns.
        var watchedFile = Path.Combine(TestDir, "watch_queued.txt").Replace("\\", "/");
        File.WriteAllText(watchedFile, "initial");

        var result = Run($$"""
            let x = 0;
            let w = fs.watch("{{watchedFile}}", (event) => {
                x = 99;
            });
            fs.writeFile("{{watchedFile}}", "trigger");
            let deadline = time.millis() + 5000;
            while (x == 0 && time.millis() < deadline) {
                time.sleep(0.05);
            }
            fs.unwatch(w);
            let result = x;
            """);

        // Parent's global x was flipped to 99 by the queued callback.
        Assert.Equal(99L, result);
    }

    [Fact]
    public void Watch_UpvalueRefType_DictMutationIsSharedViaQueuedDelivery()
    {
        // Background-thread fs.watch callbacks are enqueued and delivered on the VM thread
        // at the next time.sleep drain point.  A mutation of a reference-typed captured
        // upvalue (dict) inside the callback IS visible to the parent after the sleep
        // that drained it returns.
        var filePath = Path.Combine(TestDir, "watch_queued_upvalue.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        var result = Run($$"""
            let state = {value: 0};
            let w = fs.watch("{{filePath}}", (event) => {
                state.value = 42;
            });
            fs.writeFile("{{filePath}}", "trigger");
            let deadline = time.millis() + 5000;
            while (state.value == 0 && time.millis() < deadline) {
                time.sleep(0.05);
            }
            fs.unwatch(w);
            let result = state.value;
            """);

        // Parent's captured dict.value was mutated to 42 by the queued callback.
        Assert.Equal(42L, result);
    }
}
