using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Stash.Cli.PackageManager;
using Stash.Common;

namespace Stash.Cli.PackageManager.Commands;

/// <summary>
/// Implements <c>stash pkg outdated</c>: lists direct dependencies whose installed
/// version is older than what the registry currently publishes.
/// </summary>
/// <remarks>
/// For each direct dependency the command computes:
/// <list type="bullet">
///   <item><description><b>Current</b> — version recorded in <c>stash-lock.json</c>.</description></item>
///   <item><description><b>Wanted</b> — highest published version that satisfies the manifest constraint.</description></item>
///   <item><description><b>Latest</b> — the registry's <c>latest</c> pointer (typically the highest stable release).</description></item>
/// </list>
/// Network failures cause a non-zero exit. Registry queries run in parallel with
/// bounded concurrency.
/// </remarks>
public static class OutdatedCommand
{
    private const int MaxParallelism = 8;

    /// <summary>
    /// Executes the command.
    /// </summary>
    public static int Execute(string[] args)
    {
        string projectDir = Directory.GetCurrentDirectory();

        var manifest = PackageManifest.Load(projectDir);
        if (manifest == null)
        {
            Console.Error.WriteLine("No stash.json found in current directory.");
            return 1;
        }

        if (manifest.Dependencies == null || manifest.Dependencies.Count == 0)
        {
            Console.WriteLine("No dependencies declared.");
            return 0;
        }

        var lockFile = LockFile.Load(projectDir);

        IVersionLookup lookup;
        try
        {
            var (registryUrl, _) = RegistryResolver.Resolve(args);
            var config = UserConfig.Load();
            var entry = config.GetEntry(registryUrl);
            lookup = new RegistryClient(registryUrl, entry?.Token, entry?.RefreshToken,
                entry?.ExpiresAt, entry?.MachineId, registryUrl);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        return Run(manifest, lockFile, lookup);
    }

    /// <summary>
    /// Test seam: runs the comparison against an injected lookup.
    /// </summary>
    public static int Run(PackageManifest manifest, LockFile? lockFile, IVersionLookup lookup)
    {
        var deps = manifest.Dependencies!.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();

        var rows = new List<OutdatedRow>(deps.Count);
        var failures = new List<string>();

        Parallel.ForEach(
            deps,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism },
            kv =>
            {
                string name = kv.Key;
                string constraint = kv.Value;

                string? installed = null;
                if (lockFile != null && lockFile.Resolved.TryGetValue(name, out var entry))
                {
                    installed = string.IsNullOrEmpty(entry.Version) ? null : entry.Version;
                }

                List<SemVer> versions;
                SemVer? latest;
                try
                {
                    (versions, latest) = lookup.GetVersionsAndLatest(name);
                }
                catch (HttpRequestException ex)
                {
                    lock (failures) { failures.Add($"{name}: {ex.Message}"); }
                    return;
                }

                if (versions.Count == 0)
                {
                    // Package not on registry — skip silently (could be a private/local dep).
                    return;
                }

                SemVer? wanted = null;
                if (SemVerRange.TryParse(constraint, out var range) && range != null)
                {
                    wanted = range.FindBestMatch(versions);
                }

                string wantedStr = wanted?.ToString() ?? "(none)";
                string latestStr = latest?.ToString() ?? versions.Max()!.ToString();
                string currentStr = installed ?? "missing";

                bool changed = currentStr != wantedStr || currentStr != latestStr;
                if (changed)
                {
                    lock (rows)
                    {
                        rows.Add(new OutdatedRow(name, currentStr, wantedStr, latestStr));
                    }
                }
            });

        if (failures.Count > 0)
        {
            Console.Error.WriteLine("error: failed to query the registry for one or more packages:");
            foreach (var f in failures.OrderBy(s => s, StringComparer.Ordinal))
            {
                Console.Error.WriteLine($"  {f}");
            }
            return 1;
        }

        if (rows.Count == 0)
        {
            Console.WriteLine("All dependencies are up to date.");
            return 0;
        }

        rows.Sort((a, b) => string.Compare(a.Package, b.Package, StringComparison.Ordinal));
        Render(rows);
        return 0;
    }

    private static void Render(List<OutdatedRow> rows)
    {
        bool tty = !Console.IsOutputRedirected;
        if (!tty)
        {
            // Machine-readable: tab-separated, no color, header included.
            Console.WriteLine("Package\tCurrent\tWanted\tLatest");
            foreach (var r in rows)
            {
                Console.WriteLine($"{r.Package}\t{r.Current}\t{r.Wanted}\t{r.Latest}");
            }
            return;
        }

        int wPkg = Math.Max(7, rows.Max(r => r.Package.Length));
        int wCur = Math.Max(7, rows.Max(r => r.Current.Length));
        int wWant = Math.Max(6, rows.Max(r => r.Wanted.Length));
        int wLat = Math.Max(6, rows.Max(r => r.Latest.Length));

        string Pad(string s, int w) => s.PadRight(w);

        Console.WriteLine($"{Pad("Package", wPkg)}  {Pad("Current", wCur)}  {Pad("Wanted", wWant)}  {Pad("Latest", wLat)}");
        Console.WriteLine(new string('-', wPkg + wCur + wWant + wLat + 6));

        foreach (var r in rows)
        {
            string wantedCell = r.Current != r.Wanted
                ? Color(Pad(r.Wanted, wWant), AnsiGreen)
                : Pad(r.Wanted, wWant);
            string latestCell = r.Wanted != r.Latest
                ? Color(Pad(r.Latest, wLat), AnsiMagenta)
                : Pad(r.Latest, wLat);
            Console.WriteLine($"{Pad(r.Package, wPkg)}  {Pad(r.Current, wCur)}  {wantedCell}  {latestCell}");
        }
    }

    private const string AnsiGreen = "\x1b[32m";
    private const string AnsiMagenta = "\x1b[35m";
    private const string AnsiReset = "\x1b[0m";

    private static string Color(string text, string code) => $"{code}{text}{AnsiReset}";

    /// <summary>One row of the outdated report.</summary>
    public readonly record struct OutdatedRow(string Package, string Current, string Wanted, string Latest);
}
