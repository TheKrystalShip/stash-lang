using System;
using System.Collections.Generic;
using Stash.Bytecode;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Unit tests for <see cref="VMContext"/>'s per-VM virtual cwd and env overlay —
/// the single source of truth for hermetic process-state virtualization (phase 2B-1).
/// </summary>
public class VMContextTests
{
    // ─── WorkingDirectory ──────────────────────────────────────────────────────

    [Fact]
    public void WorkingDirectory_InitializedFromRealCwd_AtConstruction()
    {
        string realCwd = System.Environment.CurrentDirectory;
        var ctx = new VMContext(default);
        Assert.Equal(realCwd, ctx.WorkingDirectory);
    }

    [Fact]
    public void WorkingDirectory_AfterSet_ReturnsUpdatedValue_NeverReReadsRealCwd()
    {
        var ctx = new VMContext(default);
        string originalReal = System.Environment.CurrentDirectory;

        ctx.WorkingDirectory = "/tmp/fake-test-dir";

        // The VM's view is updated.
        Assert.Equal("/tmp/fake-test-dir", ctx.WorkingDirectory);

        // The real process cwd is NOT changed.
        Assert.Equal(originalReal, System.Environment.CurrentDirectory);
    }

    [Fact]
    public void WorkingDirectory_TwoVMContexts_StartWithSameRealCwd_AreIndependent()
    {
        var ctxA = new VMContext(default);
        var ctxB = new VMContext(default);

        ctxA.WorkingDirectory = "/vm-a";

        // ctxB is unaffected.
        Assert.NotEqual(ctxA.WorkingDirectory, ctxB.WorkingDirectory);
        Assert.Equal(System.Environment.CurrentDirectory, ctxB.WorkingDirectory);
    }

    // ─── GetEnv / SetEnv / UnsetEnv isolation ──────────────────────────────────

    [Fact]
    public void SetEnv_WritesToOverlayOnly_RealEnvUnchanged()
    {
        // Use a name extremely unlikely to be set in the real process env.
        const string varName = "STASH_TEST_2B1_ISOLATION_VAR_XYZ";
        string? originalReal = System.Environment.GetEnvironmentVariable(varName);

        // Pre-condition: this var should not exist in the real process env for this test to be valid.
        Assert.Null(originalReal);

        var ctx = new VMContext(default);
        ctx.SetEnv(varName, "hello");

        // VM sees the value through GetEnv.
        Assert.Equal("hello", ctx.GetEnv(varName));

        // Real process env is NOT mutated.
        Assert.Null(System.Environment.GetEnvironmentVariable(varName));
    }

    [Fact]
    public void TwoVMContexts_SetEnvOnOne_OtherGetEnvReturnsNull_RealEnvAlsoNull()
    {
        const string varName = "STASH_TEST_2B1_TWO_VM_ISOLATION_VAR";
        Assert.Null(System.Environment.GetEnvironmentVariable(varName)); // pre-condition

        var ctxA = new VMContext(default);
        var ctxB = new VMContext(default);

        ctxA.SetEnv(varName, "1");

        // ctxA sees the value.
        Assert.Equal("1", ctxA.GetEnv(varName));

        // ctxB does NOT see ctxA's write.
        Assert.Null(ctxB.GetEnv(varName));

        // Real System.Environment is also unaffected.
        Assert.Null(System.Environment.GetEnvironmentVariable(varName));
    }

    [Fact]
    public void GetEnv_FallsBackToRealEnv_ForKeyNotInOverlay()
    {
        // PATH is virtually always set in the real process env.
        string? realPath = System.Environment.GetEnvironmentVariable("PATH");
        if (realPath is null) return; // degenerate environment; skip

        var ctx = new VMContext(default);

        // No overlay entry for PATH — should fall back to real env.
        Assert.Equal(realPath, ctx.GetEnv("PATH"));
    }

    [Fact]
    public void UnsetEnv_ShadowsRealEnvKey_GetEnvReturnsNull()
    {
        // Use PATH which should be set in real env; unset it in the overlay.
        string? realPath = System.Environment.GetEnvironmentVariable("PATH");
        if (realPath is null) return; // degenerate environment; skip

        var ctx = new VMContext(default);
        ctx.UnsetEnv("PATH");

        // The overlay shadow makes GetEnv return null.
        Assert.Null(ctx.GetEnv("PATH"));

        // Real process env is NOT changed.
        Assert.Equal(realPath, System.Environment.GetEnvironmentVariable("PATH"));
    }

    [Fact]
    public void UnsetEnv_ThenSetEnv_OverridesTheShadow()
    {
        const string varName = "STASH_TEST_2B1_UNSET_THEN_SET";
        Assert.Null(System.Environment.GetEnvironmentVariable(varName)); // pre-condition

        var ctx = new VMContext(default);
        ctx.UnsetEnv(varName);
        Assert.Null(ctx.GetEnv(varName));

        ctx.SetEnv(varName, "restored");
        Assert.Equal("restored", ctx.GetEnv(varName));

        // Real env unchanged throughout.
        Assert.Null(System.Environment.GetEnvironmentVariable(varName));
    }

    // ─── AllEnv ────────────────────────────────────────────────────────────────

    [Fact]
    public void AllEnv_ContainsRealEnvVars_WhenNoOverlay()
    {
        string? realPath = System.Environment.GetEnvironmentVariable("PATH");
        if (realPath is null) return;

        var ctx = new VMContext(default);
        var all = ctx.AllEnv();

        Assert.True(all.ContainsKey("PATH"));
        Assert.Equal(realPath, all["PATH"]);
    }

    [Fact]
    public void AllEnv_OverlayWinsOverRealEnv()
    {
        const string varName = "STASH_TEST_2B1_ALLENV";
        Assert.Null(System.Environment.GetEnvironmentVariable(varName));

        var ctx = new VMContext(default);
        ctx.SetEnv(varName, "overlay-value");

        var all = ctx.AllEnv();
        Assert.True(all.ContainsKey(varName));
        Assert.Equal("overlay-value", all[varName]);

        Assert.Null(System.Environment.GetEnvironmentVariable(varName));
    }

    [Fact]
    public void AllEnv_UnsetEnvKeyNotInResult()
    {
        string? realPath = System.Environment.GetEnvironmentVariable("PATH");
        if (realPath is null) return;

        var ctx = new VMContext(default);
        ctx.UnsetEnv("PATH");

        var all = ctx.AllEnv();
        Assert.False(all.ContainsKey("PATH"));

        // Real env intact.
        Assert.Equal(realPath, System.Environment.GetEnvironmentVariable("PATH"));
    }

    // ─── ResolveAgainstCwd ─────────────────────────────────────────────────────

    [Fact]
    public void ResolveAgainstCwd_RelativePath_ResolvesAgainstWorkingDirectory()
    {
        var ctx = new VMContext(default);
        ctx.WorkingDirectory = "/base/dir";

        string resolved = ctx.ResolveAgainstCwd("subdir/file.txt");

        // Path.GetFullPath("/base/dir", "subdir/file.txt") → "/base/dir/subdir/file.txt"
        Assert.Equal(Path.GetFullPath("subdir/file.txt", "/base/dir"), resolved);
    }

    [Fact]
    public void ResolveAgainstCwd_AbsolutePath_IsReturnedUnchanged()
    {
        var ctx = new VMContext(default);
        ctx.WorkingDirectory = "/base/dir";

        string absolute = "/tmp/other/file.txt";
        string resolved = ctx.ResolveAgainstCwd(absolute);

        Assert.Equal(Path.GetFullPath(absolute, "/base/dir"), resolved);
    }

    [Fact]
    public void ResolveAgainstCwd_UsesPerVmCwd_NotRealProcessCwd()
    {
        var ctx = new VMContext(default);
        ctx.WorkingDirectory = "/virtual/cwd";

        string resolved = ctx.ResolveAgainstCwd("relative.txt");

        // Must be rooted at the per-VM cwd, NOT System.Environment.CurrentDirectory.
        Assert.StartsWith("/virtual/cwd", resolved);
        // Sanity check: this would differ if real cwd != "/virtual/cwd".
        if (System.Environment.CurrentDirectory != "/virtual/cwd")
            Assert.NotEqual(Path.GetFullPath("relative.txt"), resolved);
    }

    // ─── DirStack consistency with WorkingDirectory ────────────────────────────

    [Fact]
    public void DirStack_InitialEntry_MatchesWorkingDirectory()
    {
        var ctx = new VMContext(default);
        Assert.Single(ctx.DirStack);
        Assert.Equal(ctx.WorkingDirectory, ctx.DirStack[0]);
    }

    // ─── Fork propagates virtual state ─────────────────────────────────────────

    [Fact]
    public void Fork_Child_InheritsParentWorkingDirectory()
    {
        var parent = new VMContext(default);
        parent.WorkingDirectory = "/parent/virtual/cwd";

        var child = (VMContext)parent.Fork();

        Assert.Equal("/parent/virtual/cwd", child.WorkingDirectory);
    }

    [Fact]
    public void Fork_Child_WorkingDirectoryMutationDoesNotAffectParent()
    {
        var parent = new VMContext(default);
        parent.WorkingDirectory = "/parent/dir";

        var child = (VMContext)parent.Fork();
        child.WorkingDirectory = "/child/dir";

        Assert.Equal("/parent/dir", parent.WorkingDirectory);
    }

    [Fact]
    public void Fork_Child_InheritsEnvOverlaySnapshot()
    {
        const string varName = "STASH_TEST_2B1_FORK_INHERIT";
        Assert.Null(System.Environment.GetEnvironmentVariable(varName));

        var parent = new VMContext(default);
        parent.SetEnv(varName, "parent-value");

        var child = (VMContext)parent.Fork();

        // Child sees the parent's overlay value at fork time.
        Assert.Equal("parent-value", child.GetEnv(varName));
    }

    [Fact]
    public void Fork_Child_EnvMutationDoesNotAffectParent()
    {
        const string varName = "STASH_TEST_2B1_FORK_ISOLATE";
        Assert.Null(System.Environment.GetEnvironmentVariable(varName));

        var parent = new VMContext(default);
        parent.SetEnv(varName, "parent-value");

        var child = (VMContext)parent.Fork();
        child.SetEnv(varName, "child-value");

        // Parent's overlay is unaffected.
        Assert.Equal("parent-value", parent.GetEnv(varName));
        // Child has its own updated value.
        Assert.Equal("child-value", child.GetEnv(varName));
        // Real env still null.
        Assert.Null(System.Environment.GetEnvironmentVariable(varName));
    }
}
