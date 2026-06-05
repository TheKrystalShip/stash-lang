namespace Stash.Tests.Embedding;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Hosting;
using Xunit;

/// <summary>
/// Acceptance suite for P4: OnRelease lifetime hook and DisposeAsync ConditionalWeakTable iteration.
///
/// done_when coverage:
///   #1  — OnRelease_Registration_Compiles
///   #2  — OnRelease_FiredExactlyOnce_PerObservedTarget
///   #3  — OnRelease_NotFiredForUnobservedInstances
///   #4  — OnRelease_NotFiredTwice_WhenSameInstanceObservedMultipleTimes
///   #5  — OnRelease_MultipleDifferentTargets_EachFiredOnce
///   #6  — OnRelease_NoCallback_NoError (type without OnRelease disposes cleanly)
///   #7  — OnRelease_AlreadyDisposed_IsIdempotent (DisposeAsync twice is safe)
///   #8  — MvpResets_StillRun_AfterOnRelease (done_when #5)
///   #9  — TwoHosts_HermeticIsolation_NoSharedOnReleaseState
/// </summary>
[Collection("ProcessGlobalSlots")]
public class HostObjectLifetimeTests
{
    // ── Domain class ──────────────────────────────────────────────────────────

    private sealed class Resource
    {
        public string Id { get; }
        public bool Disposed { get; private set; }

        public Resource(string id) => Id = id;

        public void Release() => Disposed = true;

        public Resource Self() => this;
    }

    // ── #1: Registration compiles ─────────────────────────────────────────────

    [Fact]
    public void OnRelease_Registration_Compiles()
    {
        var host = new StashHost();
        var ex = Record.Exception(() =>
            host.RegisterType<Resource>(b => b.OnRelease(r => r.Release())));
        Assert.Null(ex);
    }

    // ── #2: OnRelease fired exactly once per observed target ──────────────────

    [Fact]
    public async Task OnRelease_FiredExactlyOnce_PerObservedTarget()
    {
        var releaseCount = 0;
        var host = new StashHost();
        var r = new Resource("A");

        host.RegisterType<Resource>(b => b
            .Property("id", x => x.Id)
            .OnRelease(_ => releaseCount++));

        host.SetGlobal("r", r);

        // Accessing r.id to confirm the script runs against the observed host object.
        var script = await host.CompileAsync("return r.id;");
        await host.RunAsync(script);

        await host.DisposeAsync();

        Assert.Equal(1, releaseCount);
    }

    // ── #3: OnRelease NOT fired for instances the engine never observed ────────

    [Fact]
    public async Task OnRelease_NotFiredForUnobservedInstances()
    {
        var host = new StashHost();
        var observed = new Resource("seen");
        var unobserved = new Resource("never-seen");

        var released = new List<string>();

        host.RegisterType<Resource>(b => b
            .Property("id", x => x.Id)
            .OnRelease(r => released.Add(r.Id)));

        // Only bind 'observed' — 'unobserved' is never passed to the engine.
        host.SetGlobal("r", observed);

        var script = await host.CompileAsync("return r.id;");
        await host.RunAsync(script);

        await host.DisposeAsync();

        Assert.Contains("seen", released);
        Assert.DoesNotContain("never-seen", released);
        Assert.Single(released);
    }

    // ── #4: Same instance observed multiple times → OnRelease fires only once ─

    [Fact]
    public async Task OnRelease_NotFiredTwice_WhenSameInstanceObservedMultipleTimes()
    {
        var releaseCount = 0;
        var host = new StashHost();
        var r = new Resource("B");

        host.RegisterType<Resource>(b => b
            .Property("id", x => x.Id)
            .Method("self",  (Resource x) => x.Self())
            .OnRelease(_ => releaseCount++));

        host.SetGlobal("r", r);

        // The "self" method returns the same CLR instance wrapped in a new HostHandle.
        // ConditionalWeakTable.TryAdd with the same key is idempotent — registering again
        // does NOT add a second entry; the same target appears only once.
        var script = await host.CompileAsync("""
            fn run() {
                let a = r.self();
                let b = r.self();
                return r.id;
            }
            """);
        await host.RunAsync(script);
        await host.CallAsync<string>("run");

        await host.DisposeAsync();

        // Even though r was observed via SetGlobal + two calls to .self(), OnRelease fires once.
        Assert.Equal(1, releaseCount);
    }

    // ── #5: Multiple different targets each get their own OnRelease ───────────

    [Fact]
    public async Task OnRelease_MultipleDifferentTargets_EachFiredOnce()
    {
        var host = new StashHost();
        var ra = new Resource("A");
        var rb = new Resource("B");
        var released = new List<string>();

        host.RegisterType<Resource>(b => b
            .Property("id", x => x.Id)
            .OnRelease(r => released.Add(r.Id)));

        host.SetGlobal("ra", ra);
        host.SetGlobal("rb", rb);

        var script = await host.CompileAsync("return ra.id + rb.id;");
        await host.RunAsync(script);

        await host.DisposeAsync();

        Assert.Equal(2, released.Count);
        Assert.Contains("A", released);
        Assert.Contains("B", released);
    }

    // ── #6: Type without OnRelease disposes cleanly ───────────────────────────

    [Fact]
    public async Task OnRelease_NoCallback_NoError()
    {
        var host = new StashHost();
        var r = new Resource("C");

        // Register WITHOUT OnRelease — DisposeAsync should silently skip it.
        host.RegisterType<Resource>(b => b.Property("id", x => x.Id));
        host.SetGlobal("r", r);

        var script = await host.CompileAsync("return r.id;");
        await host.RunAsync(script);

        var ex = await Record.ExceptionAsync(() => host.DisposeAsync().AsTask());
        Assert.Null(ex);
    }

    // ── #7: DisposeAsync twice is safe (idempotent) ───────────────────────────

    [Fact]
    public async Task OnRelease_AlreadyDisposed_IsIdempotent()
    {
        var releaseCount = 0;
        var host = new StashHost();
        var r = new Resource("D");

        host.RegisterType<Resource>(b => b
            .Property("id", x => x.Id)
            .OnRelease(_ => releaseCount++));

        host.SetGlobal("r", r);

        await host.DisposeAsync();
        await host.DisposeAsync(); // second dispose must not re-run OnRelease

        Assert.Equal(1, releaseCount);
    }

    // ── #8: MVP resets still run after the OnRelease loop (done_when #5) ──────

    [Fact]
    public async Task MvpResets_StillRun_AfterOnRelease()
    {
        // Verify that DisposeAsync runs BOTH the OnRelease callbacks AND the MVP's
        // static-slot resets without throwing. The MVP resets are observable via
        // StashHostDisposalTests; here we just confirm that a host with both
        // OnRelease hooks AND an observed target disposes cleanly without exception.
        var host = new StashHost();
        var r = new Resource("E");
        bool releaseFired = false;

        host.RegisterType<Resource>(b => b
            .Property("id", x => x.Id)
            .OnRelease(_ => releaseFired = true));

        host.SetGlobal("r", r);
        var script = await host.CompileAsync("return r.id;");
        await host.RunAsync(script);

        var ex = await Record.ExceptionAsync(() => host.DisposeAsync().AsTask());

        // No exception during dispose (MVP resets and OnRelease both ran).
        Assert.Null(ex);
        // OnRelease was invoked for the observed target.
        Assert.True(releaseFired);
    }

    // ── #9: Two hosts — hermetic isolation — no shared OnRelease state ────────

    [Fact]
    public async Task TwoHosts_HermeticIsolation_NoSharedOnReleaseState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var host1Count = 0;
        var host2Count = 0;

        var host1 = new StashHost();
        var host2 = new StashHost();

        var r1 = new Resource("H1");
        var r2 = new Resource("H2");

        host1.RegisterType<Resource>(b => b
            .Property("id", x => x.Id)
            .OnRelease(_ => host1Count++));

        host2.RegisterType<Resource>(b => b
            .Property("id", x => x.Id)
            .OnRelease(_ => host2Count++));

        host1.SetGlobal("r", r1);
        host2.SetGlobal("r", r2);

        var script1 = await host1.CompileAsync("return r.id;");
        var script2 = await host2.CompileAsync("return r.id;");

        await host1.RunAsync(script1, timeout.Token);
        await host2.RunAsync(script2, timeout.Token);

        // Dispose host1 — only r1 should get OnRelease; r2 is not host1's concern.
        await host1.DisposeAsync();

        Assert.Equal(1, host1Count);
        Assert.Equal(0, host2Count);

        // Dispose host2 — r2 gets OnRelease.
        await host2.DisposeAsync();

        Assert.Equal(1, host1Count);
        Assert.Equal(1, host2Count);
    }
}
