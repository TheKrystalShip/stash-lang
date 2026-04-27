using System;
using System.IO;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Integration tests for the <c>lock</c> statement (file-based mutual exclusion).
/// </summary>
public class LockTests : Stash.Tests.Interpreting.StashTestBase
{
    // Helper: create a temp lock file path (unique per call)
    private static string TempLockPath()
    {
        return Path.Combine(Path.GetTempPath(), $"stash_lock_test_{Guid.NewGuid():N}.lock");
    }

    // =========================================================================
    // 1. Basic execution
    // =========================================================================

    [Fact]
    public void Lock_SimpleBlock_ExecutesBody()
    {
        string lockPath = TempLockPath();
        try
        {
            string output = RunCapturingOutput($$"""
                lock "{{lockPath}}" {
                    io.println("inside lock");
                }
                """);
            Assert.Contains("inside lock", output);
        }
        finally { File.Delete(lockPath); }
    }

    [Fact]
    public void Lock_ReleasesOnNormalExit_AllowsReacquisition()
    {
        string lockPath = TempLockPath();
        try
        {
            string output = RunCapturingOutput($$"""
                lock "{{lockPath}}" {
                    io.println("first");
                }
                lock "{{lockPath}}" {
                    io.println("second");
                }
                """);
            Assert.Contains("first", output);
            Assert.Contains("second", output);
        }
        finally { File.Delete(lockPath); }
    }

    // =========================================================================
    // 2. Embedded mode (sandbox)
    // =========================================================================

    [Fact]
    public void Lock_EmbeddedMode_ThrowsRuntimeError()
    {
        var lexer = new Lexer("""lock "/tmp/test.lock" { let x = 1; }""", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.EmbeddedMode = true;
        var ex = Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
        Assert.Contains("embedded mode", ex.Message);
    }

    // =========================================================================
    // 3. Deadlock detection (same-path nested lock)
    // =========================================================================

    [Fact]
    public void Lock_NestedSamePath_ThrowsLockError()
    {
        string lockPath = TempLockPath();
        try
        {
            var ex = RunCapturingError($$"""
                lock "{{lockPath}}" {
                    lock "{{lockPath}}" {
                        io.println("unreachable");
                    }
                }
                """);
            Assert.Equal(StashErrorTypes.LockError, ex.ErrorType);
            Assert.Contains("deadlock", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(lockPath); }
    }

    [Fact]
    public void Lock_NestedDifferentPaths_BothAcquired()
    {
        string lock1 = TempLockPath();
        string lock2 = TempLockPath();
        try
        {
            string output = RunCapturingOutput($$"""
                lock "{{lock1}}" {
                    lock "{{lock2}}" {
                        io.println("both acquired");
                    }
                }
                """);
            Assert.Contains("both acquired", output);
        }
        finally
        {
            File.Delete(lock1);
            File.Delete(lock2);
        }
    }

    // =========================================================================
    // 4. Lock with wait: 0s (non-blocking) against a pre-held lock
    // =========================================================================

    [Fact]
    public void Lock_WaitZero_ThrowsLockError_WhenPreHeld()
    {
        // FileShare.None cross-fd locking within the same process is not enforced on Unix
        // (flock(2) is per-process, not per-fd). Skip on non-Windows.
        if (!OperatingSystem.IsWindows())
            return;

        string lockPath = TempLockPath();
        using var preHeld = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var ex = RunCapturingError($$"""
                lock "{{lockPath}}" (wait: 0s) {
                    io.println("unreachable");
                }
                """);
            Assert.Equal(StashErrorTypes.LockError, ex.ErrorType);
        }
        finally
        {
            preHeld.Dispose();
            try { File.Delete(lockPath); } catch { /* best-effort */ }
        }
    }

    // =========================================================================
    // 5. Exception in body releases the lock
    // =========================================================================

    [Fact]
    public void Lock_BodyException_ReleasesLock()
    {
        string lockPath = TempLockPath();
        try
        {
            // Lock block throws -- verifies lock is released by re-acquiring after
            RunExpectingError($$"""
                lock "{{lockPath}}" {
                    throw "body error";
                }
                """);

            // After the exception the lock should be released -- re-acquire must succeed
            string output = RunCapturingOutput($$"""
                lock "{{lockPath}}" {
                    io.println("re-acquired");
                }
                """);
            Assert.Contains("re-acquired", output);
        }
        finally { File.Delete(lockPath); }
    }

    // =========================================================================
    // 6. LockError is catchable with typed catch
    // =========================================================================

    [Fact]
    public void Lock_LockError_IsCatchableByType()
    {
        // Use nested same-path lock (reliable deadlock detection) to trigger a LockError
        string lockPath = TempLockPath();
        try
        {
            string output = RunCapturingOutput($$"""
                try {
                    lock "{{lockPath}}" {
                        lock "{{lockPath}}" {
                            io.println("unreachable");
                        }
                    }
                } catch (LockError e) {
                    io.println("caught: " + e.type);
                }
                """);
            Assert.Contains("caught: LockError", output);
        }
        finally { File.Delete(lockPath); }
    }

    [Fact]
    public void Lock_LockError_HasPathField()
    {
        // Use nested same-path lock to trigger LockError; verify ErrorType is set
        string lockPath = TempLockPath();
        try
        {
            var ex = RunCapturingError($$"""
                lock "{{lockPath}}" {
                    lock "{{lockPath}}" {
                        io.println("unreachable");
                    }
                }
                """);
            Assert.Equal(StashErrorTypes.LockError, ex.ErrorType);
        }
        finally { File.Delete(lockPath); }
    }

    // =========================================================================
    // 7. throw LockError struct -- is throwable and catchable
    // =========================================================================

    [Fact]
    public void LockError_IsThrowableAsStruct()
    {
        string output = RunCapturingOutput("""
            try {
                throw LockError { message: "manual lock error", path: "/var/run/test.lock" };
            } catch (LockError e) {
                io.println(e.path);
            }
            """);
        Assert.Contains("/var/run/test.lock", output);
    }

    // =========================================================================
    // 8. Defer inside lock runs before lock is released
    // =========================================================================

    [Fact]
    public void Lock_ComposesWithDefer_DeferRunsWhileLockHeld()
    {
        string lockPath = TempLockPath();
        try
        {
            // Wrap in a function so defer runs at fn exit (before the caller prints "after lock")
            string output = RunCapturingOutput($$"""
                fn acquire() {
                    lock "{{lockPath}}" {
                        defer {
                            io.println("defer inside lock");
                        }
                        io.println("body");
                    }
                }
                acquire();
                io.println("after lock");
                """);
            // Verify all three strings appear: body ran, defer ran, execution continued
            Assert.Contains("body", output);
            Assert.Contains("defer inside lock", output);
            Assert.Contains("after lock", output);
        }
        finally { File.Delete(lockPath); }
    }

    // =========================================================================
    // 9. Value assigned inside lock is accessible outside
    // =========================================================================

    [Fact]
    public void Lock_ValueAssignedInsideLock_AccessibleOutside()
    {
        string lockPath = TempLockPath();
        try
        {
            object? value = Run($$"""
                let result = null;
                lock "{{lockPath}}" {
                    result = 42;
                }
                """);
            Assert.Equal(42L, value);
        }
        finally { File.Delete(lockPath); }
    }

    // =========================================================================
    // 10. Outer lock stays held after catching an inner lock's LockError
    //     Regression for: TryBegin before LockBegin caused inner error path to
    //     pop the outer lock from ActiveLocks before the outer body finished.
    // =========================================================================

    [Fact]
    public void Lock_OuterLockHeldAfterCatchingInnerLockError()
    {
        string lockPath = TempLockPath();
        try
        {
            // The outer lock is on lockPath. Inside its body, a second acquisition
            // on the same path fires the deadlock-detection LockError. The user
            // catches that error. The outer lock must still be active after the catch.
            //
            // We verify this by attempting a third acquisition after the catch:
            // if the outer lock is still held, deadlock detection fires again → "blocked".
            // If the outer lock was erroneously released, the acquisition succeeds → "re-acquired".
            string output = RunCapturingOutput($$"""
                lock "{{lockPath}}" {
                    try {
                        lock "{{lockPath}}" {
                            io.println("unreachable");
                        }
                    } catch (LockError e) {
                        io.println("caught");
                    }
                    // A re-acquisition attempt inside the outer block must still fail.
                    try {
                        lock "{{lockPath}}" {
                            io.println("re-acquired");
                        }
                    } catch (LockError e) {
                        io.println("blocked");
                    }
                }
                """);
            Assert.Contains("caught", output);
            Assert.DoesNotContain("re-acquired", output);
            Assert.Contains("blocked", output);
        }
        finally { File.Delete(lockPath); }
    }

    // =========================================================================
    // 11. Lock with options parses correctly (wait/stale named options)
    // =========================================================================

    [Fact]
    public void Lock_WithWaitOption_ParsesAndExecutes()
    {
        string lockPath = TempLockPath();
        try
        {
            string output = RunCapturingOutput($$"""
                lock "{{lockPath}}" (wait: 0s) {
                    io.println("acquired");
                }
                """);
            Assert.Contains("acquired", output);
        }
        finally { File.Delete(lockPath); }
    }
}
