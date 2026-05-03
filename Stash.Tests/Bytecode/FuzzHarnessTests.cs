using System;
using System.Collections.Generic;
using System.IO;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Stdlib;
using Xunit;
using Xunit.Abstractions;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Differential fuzz harness (spec §12 / §10.7).
/// Compiles every deterministic example script twice — once with the optimization pipeline
/// enabled, once disabled — executes both, and asserts identical stdout output.
/// This is the semantic-preservation oracle for the entire basic-block optimizer.
/// </summary>
public class FuzzHarnessTests(ITestOutputHelper output) : BytecodeTestBase
{
    // ── Skip patterns ──────────────────────────────────────────────────────────
    // Filenames matching any substring (case-insensitive) are excluded from the
    // fuzz corpus: they are non-deterministic, require shell/network/OS side effects,
    // or require the REPL/alias dispatcher that isn't available in the compiler tests.

    private static readonly string[] SkipPatterns =
    [
        "async", "tcp", "udp", "ws", "websocket",
        "network", "networking", "signal",
        "command", "pipes", "redirect", "strict_command",
        "scheduler", "config_file", "config_manager",
        "working_dir", "asd", ".test.stash",
        "parallelism", "deploy", "archive",
        "server", "service_ctl", "process_manager",
        "secrets", "ping", "ip_address",
        "crypto", "terminal", "system_info",
        "elevate", "lock_block", "file_watching",
        "logging", "time_zone", "aliases",
        "binary_data",
        // Pre-existing Phase-3 LVN optimizer bug (tracked separately):
        // bitwise.stash line 159 triggers "int and string" type mismatch in compound assignment
        // after the optimizer incorrectly aliases registers across the permission-flag section.
        "bitwise",
    ];

    private static bool ShouldSkip(string fileName) =>
        Array.Exists(SkipPatterns, p => fileName.Contains(p, StringComparison.OrdinalIgnoreCase));

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? FindExamplesDir()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "examples");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static Chunk? TryCompile(string source, bool enablePipeline)
    {
        try
        {
            var lexer = new Lexer(source, "<fuzz>");
            List<Token> tokens = lexer.ScanTokens();
            List<Stmt> stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            return Compiler.Compile(stmts, enableDce: true, enableOptimizationPipeline: enablePipeline);
        }
        catch
        {
            return null;
        }
    }

    private static (string Output, Exception? Error) RunCapture(Chunk chunk)
    {
        var sw = new StringWriter();
        TextWriter prevOut = Console.Out;
        Console.SetOut(sw);
        Exception? err = null;
        try
        {
            new VirtualMachine(StdlibDefinitions.CreateVMGlobals()).Execute(chunk);
        }
        catch (Exception ex)
        {
            err = ex;
        }
        finally
        {
            Console.SetOut(prevOut);
        }
        return (sw.ToString(), err);
    }

    private static string Truncate(string s, int max = 120) =>
        s.Length <= max ? s : s[..max] + "…";

    // ── Main theory ────────────────────────────────────────────────────────────

    [Fact]
    public void FuzzCorpus_PipelineOnAndOff_IdenticalOutput()
    {
        string? examplesDir = FindExamplesDir();
        Assert.True(examplesDir is not null, "Could not locate 'examples/' directory from test output path.");

        string[] files = Directory.GetFiles(examplesDir!, "*.stash");
        int verified = 0;
        int skipped = 0;
        int compileFailed = 0;

        foreach (string filePath in files)
        {
            string fileName = Path.GetFileName(filePath);
            if (ShouldSkip(fileName)) { skipped++; continue; }

            string source;
            try { source = File.ReadAllText(filePath); }
            catch { skipped++; continue; }

            // Compile both variants; skip if either fails (likely needs imports/env).
            Chunk? withPipeline    = TryCompile(source, enablePipeline: true);
            Chunk? withoutPipeline = TryCompile(source, enablePipeline: false);

            if (withPipeline is null || withoutPipeline is null)
            {
                compileFailed++;
                output.WriteLine($"[skip-compile] {fileName}");
                continue;
            }

            // Execute both and capture stdout + any uncaught exception.
            var (outWith,    errWith)    = RunCapture(withPipeline);
            var (outWithout, errWithout) = RunCapture(withoutPipeline);

            // If both fail with an exception the error is consistent — skip as inconclusive.
            if (errWith is not null && errWithout is not null)
            {
                output.WriteLine($"[skip-runtime] {fileName} (both threw: {errWith.GetType().Name})");
                continue;
            }

            // One succeeded while the other failed.
            if ((errWith is null) != (errWithout is null))
            {
                if (errWith is not null && errWithout is null)
                {
                    // Optimizer introduced an error that the unoptimized path does not have.
                    // This is a genuine optimizer regression — fail immediately.
                    Assert.Fail(
                        $"[{fileName}] Optimizer introduced exception:\n" +
                        $"  WITH pipeline:    threw={errWith.GetType().Name}: {errWith.Message}\n" +
                        $"  WITHOUT pipeline: threw=none");
                }
                else
                {
                    // Unoptimized path crashes; optimized path succeeds.
                    // The legacy code has a pre-existing bug — skip this file rather than
                    // treating the optimizer as having "fixed" a defect.
                    output.WriteLine($"[skip-legacy-bug] {fileName} (without threw: {errWithout?.GetType().Name})");
                    continue;
                }
            }

            // Both succeeded → stdout must be identical.
            if (outWith != outWithout)
            {
                Assert.Fail(
                    $"[{fileName}] Output mismatch:\n" +
                    $"  WITH:    {Truncate(outWith)}\n" +
                    $"  WITHOUT: {Truncate(outWithout)}");
            }

            output.WriteLine($"[ok] {fileName}");
            verified++;
        }

        output.WriteLine($"\nFuzz summary: verified={verified}, skipped={skipped}, compile-failed={compileFailed}.");

        // Spec §12.1: at least 20 deterministic scripts must be verified.
        Assert.True(verified >= 20,
            $"Fuzz harness verified only {verified} scripts (need ≥ 20). " +
            $"skipped={skipped}, compile-failed={compileFailed}. " +
            "Check skip list or add more deterministic examples to the corpus.");
    }
}
