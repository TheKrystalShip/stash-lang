using System;
using System.Collections.Generic;
using System.Net.Http;
using Stash.Cli.PackageManager;
using Stash.Cli.PackageManager.Commands;
using Stash.Common;
using Xunit;

namespace Stash.Tests.Cli;

[Collection("CliTests")]
public class OutdatedCommandTests
{
    private sealed class FakeLookup : IVersionLookup
    {
        private readonly Dictionary<string, (List<SemVer> Versions, SemVer? Latest)> _data;
        private readonly HashSet<string> _failures;

        public FakeLookup(
            Dictionary<string, (List<SemVer> Versions, SemVer? Latest)> data,
            HashSet<string>? failures = null)
        {
            _data = data;
            _failures = failures ?? new HashSet<string>();
        }

        public (List<SemVer> Versions, SemVer? Latest) GetVersionsAndLatest(string packageName)
        {
            if (_failures.Contains(packageName))
            {
                throw new HttpRequestException($"simulated failure for {packageName}");
            }
            if (_data.TryGetValue(packageName, out var v)) return v;
            return (new List<SemVer>(), null);
        }
    }

    private static SemVer V(string s) => SemVer.Parse(s)!;

    private static PackageManifest Manifest(params (string name, string constraint)[] deps)
    {
        var m = new PackageManifest { Name = "demo", Version = "0.0.1", Dependencies = new Dictionary<string, string>() };
        foreach (var (n, c) in deps) m.Dependencies[n] = c;
        return m;
    }

    private static LockFile Lock(params (string name, string version)[] entries)
    {
        var lf = new LockFile();
        foreach (var (n, v) in entries)
        {
            lf.Resolved[n] = new LockFileEntry { Version = v };
        }
        return lf;
    }

    [Fact]
    public void AllUpToDate_ExitsZero()
    {
        var manifest = Manifest(("foo", "^1.0.0"));
        var lf = Lock(("foo", "1.2.0"));
        var lookup = new FakeLookup(new()
        {
            ["foo"] = (new List<SemVer> { V("1.0.0"), V("1.1.0"), V("1.2.0") }, V("1.2.0"))
        });

        var output = CaptureStdout(() => Assert.Equal(0, OutdatedCommand.Run(manifest, lf, lookup)));
        Assert.Contains("up to date", output);
    }

    [Fact]
    public void PatchBehind_ReportsWanted()
    {
        var manifest = Manifest(("foo", "^1.2.0"));
        var lf = Lock(("foo", "1.2.0"));
        var lookup = new FakeLookup(new()
        {
            ["foo"] = (new List<SemVer> { V("1.2.0"), V("1.2.1") }, V("1.2.1"))
        });

        var output = CaptureStdout(() => Assert.Equal(0, OutdatedCommand.Run(manifest, lf, lookup)));
        Assert.Contains("foo", output);
        Assert.Contains("1.2.1", output);
    }

    [Fact]
    public void MajorBehind_ReportsBoth()
    {
        var manifest = Manifest(("foo", "^1.2.0"));
        var lf = Lock(("foo", "1.2.5"));
        var lookup = new FakeLookup(new()
        {
            ["foo"] = (new List<SemVer> { V("1.2.0"), V("1.2.5"), V("2.0.0") }, V("2.0.0"))
        });

        var output = CaptureStdout(() => Assert.Equal(0, OutdatedCommand.Run(manifest, lf, lookup)));
        // Wanted should still be 1.2.5; Latest should be 2.0.0.
        Assert.Contains("foo", output);
        Assert.Contains("2.0.0", output);
    }

    [Fact]
    public void NetworkFailure_ExitsNonZero()
    {
        var manifest = Manifest(("foo", "^1.0.0"));
        var lf = Lock(("foo", "1.0.0"));
        var lookup = new FakeLookup(
            new() { ["foo"] = (new List<SemVer> { V("1.0.0"), V("1.1.0") }, V("1.1.0")) },
            failures: new HashSet<string> { "foo" });

        var (stdout, stderr) = CaptureBoth(() => Assert.Equal(1, OutdatedCommand.Run(manifest, lf, lookup)));
        Assert.Contains("failed to query the registry", stderr);
        Assert.Contains("foo", stderr);
    }

    [Fact]
    public void MissingFromLock_ReportedAsMissing()
    {
        var manifest = Manifest(("foo", "^1.0.0"));
        var lf = Lock();  // empty
        var lookup = new FakeLookup(new()
        {
            ["foo"] = (new List<SemVer> { V("1.0.0"), V("1.1.0") }, V("1.1.0"))
        });

        var output = CaptureStdout(() => Assert.Equal(0, OutdatedCommand.Run(manifest, lf, lookup)));
        Assert.Contains("missing", output);
        Assert.Contains("foo", output);
    }

    private static string CaptureStdout(Action a)
    {
        var sw = new System.IO.StringWriter();
        var prev = Console.Out;
        Console.SetOut(sw);
        try { a(); } finally { Console.SetOut(prev); }
        return sw.ToString();
    }

    private static (string Stdout, string Stderr) CaptureBoth(Action a)
    {
        var so = new System.IO.StringWriter();
        var se = new System.IO.StringWriter();
        var po = Console.Out;
        var pe = Console.Error;
        Console.SetOut(so);
        Console.SetError(se);
        try { a(); } finally { Console.SetOut(po); Console.SetError(pe); }
        return (so.ToString(), se.ToString());
    }
}
