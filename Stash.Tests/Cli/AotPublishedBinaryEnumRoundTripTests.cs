using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Closes the shared-contracts residual gap: runs the <em>published Native-AOT</em> CLI binary
/// with <c>--self-test enums</c> and asserts exit 0 + "PASS" output.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test is load-bearing.</b> An in-process round-trip (see <see cref="EnumRoundTripTests"/>)
/// exercises the source-gen <c>CliJsonContext</c> under JIT, where the runtime can fall back to
/// reflection when source-gen metadata is missing — so a JIT test can pass even when
/// <c>[JsonSerializable(typeof(EnumT))]</c> is absent. Only the published AOT binary forces the
/// trim/AOT path: if source-gen metadata is absent for a type, serialization either fails or
/// silently returns <c>null</c>. The subprocess approach is the only sound end-to-end verification.
/// </para>
/// <para>
/// <b>Publish output.</b> The test discovers the binary under <c>.bench-bin/</c> relative to the
/// solution root. The verify command in <c>plan.yaml</c> runs
/// <c>dotnet publish Stash.Cli/Stash.Cli.csproj -c Release -o .bench-bin</c> before this test, so
/// the binary is guaranteed to exist when the test runs inside the verify-phase gate.
/// </para>
/// <para>
/// <b>Binary name.</b> On Linux/macOS the binary is <c>stash</c>; on Windows it is <c>stash.exe</c>.
/// </para>
/// </remarks>
[Collection("CliTests")]
public sealed class AotPublishedBinaryEnumRoundTripTests
{
    /// <summary>
    /// Publishes (or reuses) the Native-AOT binary and invokes it with <c>--self-test enums</c>,
    /// asserting exit code 0 and stdout containing "PASS".
    /// </summary>
    [Fact]
    public void AotBinary_SelfTestEnums_PrintsPassAndExitsZero()
    {
        string binaryPath = ResolveBinaryPath();

        if (!File.Exists(binaryPath))
        {
            // Publish the binary on demand so this test is self-sufficient on a clean
            // checkout (no .bench-bin artifact required before dotnet test). This mirrors
            // the verify-phase gate's behavior without coupling the test to an ordering
            // invariant outside dotnet test.
            string? solutionRoot = FindSolutionRoot(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? AppContext.BaseDirectory);

            if (solutionRoot == null)
            {
                Assert.Fail(
                    $"Cannot locate solution root to run dotnet publish. " +
                    $"Binary not found at: {binaryPath}");
            }

            string cliProject = Path.Combine(solutionRoot!, "Stash.Cli", "Stash.Cli.csproj");
            string outputDir = Path.GetDirectoryName(binaryPath)!;

            using var publishProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"publish \"{cliProject}\" -c Release -o \"{outputDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = solutionRoot!,
                }
            };
            publishProc.Start();
            string pubOut = publishProc.StandardOutput.ReadToEnd();
            string pubErr = publishProc.StandardError.ReadToEnd();
            publishProc.WaitForExit(300_000); // 5 min timeout for cold AOT compile

            if (!File.Exists(binaryPath))
            {
                Assert.Fail(
                    $"dotnet publish completed but binary still not found at: {binaryPath}\n\n" +
                    $"stdout:\n{pubOut}\n" +
                    $"stderr:\n{pubErr}");
            }
        }

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = "--self-test enums",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        proc.Start();
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);

        int exitCode = proc.ExitCode;

        Assert.True(
            exitCode == 0,
            $"The Native-AOT CLI binary exited with code {exitCode} (expected 0).\n\n" +
            $"stdout:\n{stdout}\n" +
            $"stderr:\n{stderr}\n\n" +
            $"This means at least one bounded-domain enum value failed to serialize correctly " +
            $"through the source-gen CliJsonContext under Native AOT. " +
            $"Check that every enum type has [JsonSerializable(typeof(EnumT))] in CliJsonContext.cs " +
            $"and that [JsonStringEnumMemberName] is present on every enum member.");

        Assert.Contains(
            "PASS",
            stdout,
            StringComparison.Ordinal);
    }

    // ── Binary resolution ──────────────────────────────────────────────────────

    private static string ResolveBinaryPath()
    {
        // Walk up from the test assembly's location to find the solution root,
        // then construct the path to .bench-bin/stash (or stash.exe on Windows).
        string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? AppContext.BaseDirectory;

        string? solutionRoot = FindSolutionRoot(assemblyDir);
        if (solutionRoot == null)
        {
            // Fallback: use assembly dir as a base (will not find the binary but gives a clear error)
            solutionRoot = assemblyDir;
        }

        // The published binary is named after the AssemblyName in Stash.Cli.csproj,
        // which is "Stash" (capital S). On Windows it gets .exe; on other platforms no suffix.
        string binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Stash.exe"
            : "Stash";

        return Path.Combine(solutionRoot, ".bench-bin", binaryName);
    }

    private static string? FindSolutionRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            // Stash.sln lives at the solution root
            if (File.Exists(Path.Combine(dir.FullName, "Stash.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}
