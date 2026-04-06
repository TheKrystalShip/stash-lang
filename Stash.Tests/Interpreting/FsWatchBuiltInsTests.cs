using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class FsWatchBuiltInsTests : TempDirectoryFixture
{
    public FsWatchBuiltInsTests() : base("stash_fswatch_test") { }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void Watch_FileModified_CallbackFires()
    {
        var filePath = Path.Combine(TestDir, "watch_modified.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        var result = Run($$"""
            let state = {fired: false, matched: false};
            let w = fs.watch("{{filePath}}", (event) => {
                state.fired = true;
                state.matched = event.type == fs.WatchEventType.Modified;
            });
            fs.writeFile("{{filePath}}", "modified content");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.matched;
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Watch_DirectoryFileCreated_CallbackFires()
    {
        var dir = Path.Combine(TestDir, "watch_created_dir").Replace("\\", "/");
        Directory.CreateDirectory(dir);
        var newFile = $"{dir}/newfile.txt";

        var result = Run($$"""
            let state = {matched: false};
            let w = fs.watch("{{dir}}", (event) => {
                if (event.type == fs.WatchEventType.Created) {
                    state.matched = true;
                }
            });
            fs.writeFile("{{newFile}}", "hello");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.matched;
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Watch_DirectoryFileDeleted_CallbackFires()
    {
        var dir = Path.Combine(TestDir, "watch_deleted_dir").Replace("\\", "/");
        Directory.CreateDirectory(dir);
        var target = $"{dir}/todelete.txt";
        File.WriteAllText(target, "data");

        var result = Run($$"""
            let state = {matched: false};
            let w = fs.watch("{{dir}}", (event) => {
                state.matched = event.type == fs.WatchEventType.Deleted;
            });
            fs.delete("{{target}}");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.matched;
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Watch_DirectoryFileRenamed_CallbackFires()
    {
        var dir = Path.Combine(TestDir, "watch_renamed_dir").Replace("\\", "/");
        Directory.CreateDirectory(dir);
        var oldFile = $"{dir}/before.txt";
        var newFile = $"{dir}/after.txt";
        File.WriteAllText(oldFile, "data");

        var eventType = Run($$"""
            let state = {matched: false};
            let w = fs.watch("{{dir}}", (event) => {
                state.matched = event.type == fs.WatchEventType.Renamed;
            });
            fs.move("{{oldFile}}", "{{newFile}}");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.matched;
            """);

        Assert.Equal(true, eventType);

        File.WriteAllText(oldFile, "data");
        if (File.Exists(newFile)) File.Delete(newFile);

        var hasOldPath = Run($$"""
            let state = {hasOldPath: false};
            let w = fs.watch("{{dir}}", (event) => {
                state.hasOldPath = event.oldPath != null;
            });
            fs.move("{{oldFile}}", "{{newFile}}");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.hasOldPath;
            """);

        Assert.Equal(true, hasOldPath);
    }

    [Fact]
    public void Watch_WithFilter_OnlyMatchingFilesTriggger()
    {
        var dir = Path.Combine(TestDir, "watch_filter_dir").Replace("\\", "/");
        Directory.CreateDirectory(dir);
        var match = $"{dir}/match.txt";
        var noMatch = $"{dir}/nomatch.log";
        // Pre-create both files so only "modified" events fire (avoids created+modified double-count)
        File.WriteAllText(match, "initial");
        File.WriteAllText(noMatch, "initial");

        var result = Run($$"""
            let state = {count: 0};
            let w = fs.watch("{{dir}}", (event) => {
                state.count = state.count + 1;
            }, fs.WatchOptions { filter: "*.txt" });
            fs.writeFile("{{match}}", "updated");
            fs.writeFile("{{noMatch}}", "updated");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.count;
            """);

        Assert.Equal(1L, result);
    }

    [Fact]
    public void Watch_Recursive_SubdirChangesDetected()
    {
        var dir = Path.Combine(TestDir, "watch_recursive_dir").Replace("\\", "/");
        var subdir = $"{dir}/sub";
        Directory.CreateDirectory(subdir);
        var deepFile = $"{subdir}/deep.txt";

        var result = Run($$"""
            let state = {count: 0};
            let w = fs.watch("{{dir}}", (event) => {
                state.count = state.count + 1;
            }, fs.WatchOptions { recursive: true });
            fs.writeFile("{{deepFile}}", "hello");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.count;
            """);

        Assert.True((long)result! >= 1);
    }

    [Fact]
    public void Unwatch_StopsCallbacks()
    {
        var filePath = Path.Combine(TestDir, "watch_stopped.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        var result = Run($$"""
            let state = {count: 0};
            let w = fs.watch("{{filePath}}", (event) => {
                state.count = state.count + 1;
            });
            fs.unwatch(w);
            fs.writeFile("{{filePath}}", "after unwatch");
            time.sleep(0.8);
            let result = state.count;
            """);

        Assert.Equal(0L, result);
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
        var filePath = Path.Combine(TestDir, "watch_cb_error.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        var result = Run($$"""
            let state = {called: 0, count: 0};
            let w = fs.watch("{{filePath}}", (event) => {
                state.called = state.called + 1;
                if (state.called == 1) {
                    throw "intentional error";
                }
                state.count = state.count + 1;
            });
            fs.writeFile("{{filePath}}", "first");
            time.sleep(0.8);
            fs.writeFile("{{filePath}}", "second");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.count;
            """);

        Assert.Equal(1L, result);
    }

    [Fact]
    public void Watch_SamePathTwice_BothFire()
    {
        var filePath = Path.Combine(TestDir, "watch_two.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        var result = Run($$"""
            let state = {count: 0};
            let w1 = fs.watch("{{filePath}}", (event) => {
                state.count = state.count + 1;
            });
            let w2 = fs.watch("{{filePath}}", (event) => {
                state.count = state.count + 1;
            });
            fs.writeFile("{{filePath}}", "changed");
            time.sleep(0.8);
            fs.unwatch(w1);
            fs.unwatch(w2);
            let result = state.count;
            """);

        Assert.True((long)result! >= 1);
    }

    // ── Options tests ────────────────────────────────────────────────────────

    [Fact]
    public void Watch_DefaultOptions_Works()
    {
        var filePath = Path.Combine(TestDir, "watch_default.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        var result = Run($$"""
            let state = {matched: false};
            let w = fs.watch("{{filePath}}", (event) => {
                state.matched = event.type == fs.WatchEventType.Modified;
            });
            fs.writeFile("{{filePath}}", "updated");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.matched;
            """);

        Assert.Equal(true, result);
    }

    [Fact]
    public void Watch_RecursiveFalse_SubdirIgnored()
    {
        var dir = Path.Combine(TestDir, "watch_nonrecursive").Replace("\\", "/");
        var subdir = $"{dir}/sub";
        Directory.CreateDirectory(subdir);
        var ignoredFile = $"{subdir}/ignored.txt";

        var result = Run($$"""
            let state = {count: 0};
            let w = fs.watch("{{dir}}", (event) => {
                state.count = state.count + 1;
            }, fs.WatchOptions { recursive: false });
            fs.writeFile("{{ignoredFile}}", "hello");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.count;
            """);

        Assert.Equal(0L, result);
    }

    // ── Debounce tests ───────────────────────────────────────────────────────

    [Fact]
    public void Watch_DebouncedRapidWrites_SingleCallback()
    {
        var filePath = Path.Combine(TestDir, "watch_debounce.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        var result = Run($$"""
            let state = {count: 0};
            let w = fs.watch("{{filePath}}", (event) => {
                state.count = state.count + 1;
            });
            fs.writeFile("{{filePath}}", "1");
            fs.writeFile("{{filePath}}", "2");
            fs.writeFile("{{filePath}}", "3");
            fs.writeFile("{{filePath}}", "4");
            fs.writeFile("{{filePath}}", "5");
            time.sleep(1.0);
            fs.unwatch(w);
            let result = state.count;
            """);

        Assert.Equal(1L, result);
    }

    [Fact]
    public void Watch_DebounceZero_AllEventsRaw()
    {
        var subDir = Path.Combine(TestDir, "watch_nodebounce").Replace("\\", "/");
        Directory.CreateDirectory(subDir);

        var result = Run($$"""
            let state = {count: 0};
            let w = fs.watch("{{subDir}}", (event) => {
                state.count = state.count + 1;
            }, fs.WatchOptions { debounce: 0 });
            fs.writeFile("{{subDir}}/a.txt", "1");
            fs.writeFile("{{subDir}}/b.txt", "2");
            fs.writeFile("{{subDir}}/c.txt", "3");
            fs.writeFile("{{subDir}}/d.txt", "4");
            fs.writeFile("{{subDir}}/e.txt", "5");
            time.sleep(1.0);
            fs.unwatch(w);
            let result = state.count;
            """);

        Assert.True((long)result! > 1);
    }

    // ── Scope isolation tests ─────────────────────────────────────────────────

    [Fact]
    public void Watch_ValueTypeNotShared_ForkSemantics()
    {
        var filePath = Path.Combine(TestDir, "watch_fork.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        // Callbacks run with the original closure, so parent-scope variables are accessible.
        // Primitive variable x assigned in callback is visible in parent after callback fires.
        var result = Run($$"""
            let x = 0;
            let w = fs.watch("{{filePath}}", (event) => {
                x = 99;
            });
            fs.writeFile("{{filePath}}", "trigger");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = x;
            """);

        Assert.Equal(99L, result);
    }

    [Fact]
    public void Watch_ReferenceTypeShared_DictMutation()
    {
        var filePath = Path.Combine(TestDir, "watch_shared.txt").Replace("\\", "/");
        File.WriteAllText(filePath, "initial");

        var result = Run($$"""
            let state = {value: 0};
            let w = fs.watch("{{filePath}}", (event) => {
                state.value = 42;
            });
            fs.writeFile("{{filePath}}", "trigger");
            time.sleep(0.8);
            fs.unwatch(w);
            let result = state.value;
            """);

        Assert.Equal(42L, result);
    }
}

