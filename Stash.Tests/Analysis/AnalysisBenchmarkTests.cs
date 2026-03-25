using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Lsp.Analysis;
using Xunit;
using Xunit.Abstractions;

namespace Stash.Tests.Analysis;

public class AnalysisBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    private const int WarmupIterations = 3;
    private const int MeasuredIterations = 20;

    private const int StageWarmup = 5;
    private const int StageIterations = 50;

    private const int LookupWarmup = 3;
    private const int LookupIterations = 20;

    public AnalysisBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Stash.sln")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repo root (Stash.sln)");
    }

    private static List<(string Name, string Source, int Lines)> LoadExampleScripts()
    {
        var root = FindRepoRoot();
        var examplesDir = Path.Combine(root, "examples");
        var scripts = new List<(string Name, string Source, int Lines)>();
        foreach (var file in Directory.GetFiles(examplesDir, "*.stash", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetRelativePath(examplesDir, file);
            var lines = source.Split('\n').Length;
            scripts.Add((name, source, lines));
        }
        return scripts.OrderBy(s => s.Lines).ToList();
    }

    [Fact]
    public void FullPipeline_AllExamples_MeasureResponseTime()
    {
        var scripts = LoadExampleScripts();
        var results = new List<(string Name, int Lines, double[] Times)>();

        foreach (var (name, source, lines) in scripts)
        {
            var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
            var uri = new Uri($"file:///bench/{name}");

            // Warmup
            for (int i = 0; i < WarmupIterations; i++)
            {
                engine.Analyze(uri, source);
            }

            // Measured
            var times = new double[MeasuredIterations];
            for (int i = 0; i < MeasuredIterations; i++)
            {
                var sw = Stopwatch.StartNew();
                engine.Analyze(uri, source);
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            results.Add((name, lines, times));
        }

        // Print table
        int nameWidth = Math.Max(results.Max(r => r.Name.Length), "File".Length);
        string header = $"╔{{0}}╦{{1}}╦{{2}}╦{{3}}╦{{4}}╦{{5}}╗";
        string sep    = $"╠{{0}}╬{{1}}╬{{2}}╬{{3}}╬{{4}}╬{{5}}╣";
        string row    = $"║{{0}}║{{1}}║{{2}}║{{3}}║{{4}}║{{5}}║";
        string footer = $"╚{{0}}╩{{1}}╩{{2}}╩{{3}}╩{{4}}╩{{5}}╝";

        string Pad(string s, int w) => s.PadRight(w);

        int w0 = nameWidth + 2;
        int w1 = 7;
        int w2 = 10;
        int w3 = 10;
        int w4 = 10;
        int w5 = 10;

        string Bar(char c, int w) => new string(c, w);

        _output.WriteLine(string.Format(header, Bar('═', w0), Bar('═', w1), Bar('═', w2), Bar('═', w3), Bar('═', w4), Bar('═', w5)));
        _output.WriteLine(string.Format(row,
            $" {Pad("File", nameWidth)} ",
            $" {"Lines",5} ",
            $" {"Avg (ms)",8} ",
            $" {"Min (ms)",8} ",
            $" {"Max (ms)",8} ",
            $" {"P95 (ms)",8} "));
        _output.WriteLine(string.Format(sep, Bar('═', w0), Bar('═', w1), Bar('═', w2), Bar('═', w3), Bar('═', w4), Bar('═', w5)));

        var allTimes = new List<double>();

        foreach (var (name, lines, times) in results)
        {
            var sorted = times.OrderBy(t => t).ToArray();
            double avg = times.Average();
            double min = sorted[0];
            double max = sorted[sorted.Length - 1];
            double p95 = sorted[(int)(sorted.Length * 0.95)];
            allTimes.AddRange(times);

            _output.WriteLine(string.Format(row,
                $" {Pad(name, nameWidth)} ",
                $" {lines,5} ",
                $" {avg,8:F2}  ",
                $" {min,8:F2}  ",
                $" {max,8:F2}  ",
                $" {p95,8:F2}  "));
        }

        _output.WriteLine(string.Format(sep, Bar('═', w0), Bar('═', w1), Bar('═', w2), Bar('═', w3), Bar('═', w4), Bar('═', w5)));

        var allSorted = allTimes.OrderBy(t => t).ToArray();
        double overallAvg = allTimes.Average();
        double overallMin = allSorted[0];
        double overallMax = allSorted[allSorted.Length - 1];
        double overallP95 = allSorted[(int)(allSorted.Length * 0.95)];

        _output.WriteLine(string.Format(row,
            $" {Pad("ALL FILES AVERAGE", nameWidth)} ",
            $" {"",5} ",
            $" {overallAvg,8:F2}  ",
            $" {overallMin,8:F2}  ",
            $" {overallMax,8:F2}  ",
            $" {overallP95,8:F2}  "));
        _output.WriteLine(string.Format(footer, Bar('═', w0), Bar('═', w1), Bar('═', w2), Bar('═', w3), Bar('═', w4), Bar('═', w5)));

        // Summary
        int totalLines = results.Sum(r => r.Lines);
        int totalFiles = results.Count;
        double overallP99 = allSorted[(int)(allSorted.Length * 0.99)];
        double throughput = totalLines / (overallAvg == 0 ? 0.001 : overallAvg);

        _output.WriteLine("");
        _output.WriteLine($"Total corpus: {totalLines:N0} lines across {totalFiles} files");
        _output.WriteLine($"Average analysis time: {overallAvg:F3} ms");
        _output.WriteLine($"P95 analysis time: {overallP95:F3} ms");
        _output.WriteLine($"P99 analysis time: {overallP99:F3} ms");
        _output.WriteLine($"Throughput: {throughput:N0} lines/ms");

        Assert.True(overallAvg < 5.0, $"Average analysis time {overallAvg:F3} ms exceeded 5 ms threshold");
        Assert.True(overallP95 < 10.0, $"P95 analysis time {overallP95:F3} ms exceeded 10 ms threshold");
    }

    [Fact]
    public void PipelineStageBreakdown_LargestFile()
    {
        var scripts = LoadExampleScripts();
        var (name, source, lines) = scripts.OrderByDescending(s => s.Lines).First();

        double MeasureStage(Action warmup, Action measure)
        {
            for (int i = 0; i < StageWarmup; i++)
            {
                warmup();
            }

            var times = new double[StageIterations];
            for (int i = 0; i < StageIterations; i++)
            {
                var sw = Stopwatch.StartNew();
                measure();
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }
            return times.Average();
        }

        // Pre-compute dependencies once so each stage can be measured in isolation
        var lexer0 = new Lexer(source, "<bench>", preserveTrivia: true);
        var allTokens = lexer0.ScanTokens();
        var parserTokens0 = FilterParserTokens(allTokens);
        var stmts0 = new Parser(new List<Token>(parserTokens0)).ParseProgram();
        var scopeTree0 = new SymbolCollector().Collect(stmts0);

        // Stage 1: Lexer
        double lexerAvg = MeasureStage(
            () => new Lexer(source, "<bench>", preserveTrivia: true).ScanTokens(),
            () => new Lexer(source, "<bench>", preserveTrivia: true).ScanTokens());

        // Stage 2: Token filtering
        double filterAvg = MeasureStage(
            () => FilterParserTokens(allTokens),
            () => FilterParserTokens(allTokens));

        // Stage 3: Parser
        double parserAvg = MeasureStage(
            () => new Parser(new List<Token>(parserTokens0)).ParseProgram(),
            () => new Parser(new List<Token>(parserTokens0)).ParseProgram());

        // Stage 4: SymbolCollector
        double collectorAvg = MeasureStage(
            () => new SymbolCollector().Collect(stmts0),
            () => new SymbolCollector().Collect(stmts0));

        // Stage 5: TypeInference
        double typeInferenceAvg = MeasureStage(
            () => TypeInferenceEngine.InferTypes(new SymbolCollector().Collect(stmts0), stmts0),
            () => TypeInferenceEngine.InferTypes(new SymbolCollector().Collect(stmts0), stmts0));

        // Stage 6: SemanticValidator
        double validatorAvg = MeasureStage(
            () => new SemanticValidator(scopeTree0).Validate(stmts0),
            () => new SemanticValidator(scopeTree0).Validate(stmts0));

        // Full pipeline via AnalysisEngine.Analyze
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var uri = new Uri($"file:///bench/{name}");
        double fullPipelineAvg = MeasureStage(
            () => engine.Analyze(uri, source),
            () => engine.Analyze(uri, source));

        double stageSum = lexerAvg + filterAvg + parserAvg + collectorAvg + typeInferenceAvg + validatorAvg;
        double total = stageSum == 0 ? 0.001 : stageSum;

        string Pct(double v) => $"{v / total * 100.0,5:F1}%";

        _output.WriteLine($"Pipeline Stage Breakdown — {name} ({lines} lines)");
        _output.WriteLine(new string('═', 54));
        _output.WriteLine($"  Lexer:              {lexerAvg:F3} ms avg ({Pct(lexerAvg)} of total)");
        _output.WriteLine($"  Token Filter:       {filterAvg:F3} ms avg ({Pct(filterAvg)} of total)");
        _output.WriteLine($"  Parser:             {parserAvg:F3} ms avg ({Pct(parserAvg)} of total)");
        _output.WriteLine($"  SymbolCollector:    {collectorAvg:F3} ms avg ({Pct(collectorAvg)} of total)");
        _output.WriteLine($"  TypeInference:      {typeInferenceAvg:F3} ms avg ({Pct(typeInferenceAvg)} of total)");
        _output.WriteLine($"  SemanticValidator:  {validatorAvg:F3} ms avg ({Pct(validatorAvg)} of total)");
        _output.WriteLine($"  {new string('─', 48)}");
        _output.WriteLine($"  Sum of stages:      {stageSum:F3} ms");
        _output.WriteLine($"  Full pipeline:      {fullPipelineAvg:F3} ms (via AnalysisEngine.Analyze)");
    }

    [Fact]
    public void FindDefinition_Throughput()
    {
        var scripts = LoadExampleScripts();
        var (name, source, lines) = scripts.OrderByDescending(s => s.Lines).First();

        // Build pipeline to get scopeTree and identifiers
        var lexer = new Lexer(source, "<bench>", preserveTrivia: true);
        var allTokens = lexer.ScanTokens();
        var parserTokens = FilterParserTokens(allTokens);
        var stmts = new Parser(new List<Token>(parserTokens)).ParseProgram();
        var scopeTree = new SymbolCollector().Collect(stmts);

        var identifiers = allTokens
            .Where(t => t.Type == TokenType.Identifier)
            .ToList();

        // Warmup
        for (int w = 0; w < LookupWarmup; w++)
        {
            foreach (var tok in identifiers)
            {
                scopeTree.FindDefinition(tok.Lexeme, tok.Span.StartLine, tok.Span.StartColumn);
            }
        }

        // Measure
        int totalLookups = identifiers.Count * LookupIterations;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < LookupIterations; i++)
        {
            foreach (var tok in identifiers)
            {
                scopeTree.FindDefinition(tok.Lexeme, tok.Span.StartLine, tok.Span.StartColumn);
            }
        }
        sw.Stop();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double avgPerLookupUs = (totalMs / totalLookups) * 1000.0;
        double throughputPerSec = totalLookups / (totalMs / 1000.0);

        _output.WriteLine($"FindDefinition Throughput — {name}");
        _output.WriteLine(new string('═', 46));
        _output.WriteLine($"  Identifiers in file:     {identifiers.Count:N0}");
        _output.WriteLine($"  Total lookups ({LookupIterations} iter): {totalLookups:N0}");
        _output.WriteLine($"  Total time:              {totalMs:F3} ms");
        _output.WriteLine($"  Avg per-lookup:          {avgPerLookupUs:F3} µs");
        _output.WriteLine($"  Throughput:              {throughputPerSec:N0} lookups/sec");

        Assert.True(avgPerLookupUs < 5.0,
            $"Average FindDefinition time {avgPerLookupUs:F3} µs exceeded 5 µs threshold");
    }

    [Fact]
    public void ColdVsWarm_AnalysisEngine()
    {
        var scripts = LoadExampleScripts();
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);

        var coldTimes = new double[scripts.Count];
        var warmTimes = new double[scripts.Count];

        for (int i = 0; i < scripts.Count; i++)
        {
            var (name, source, _) = scripts[i];
            var uri = new Uri($"file:///bench/{name}");

            // Cold: first call per file
            var swCold = Stopwatch.StartNew();
            engine.Analyze(uri, source);
            swCold.Stop();
            coldTimes[i] = swCold.Elapsed.TotalMilliseconds;

            // Warm: second call per file
            var swWarm = Stopwatch.StartNew();
            engine.Analyze(uri, source);
            swWarm.Stop();
            warmTimes[i] = swWarm.Elapsed.TotalMilliseconds;
        }

        double coldAvg = coldTimes.Average();
        double warmAvg = warmTimes.Average();
        var warmSorted = warmTimes.OrderBy(t => t).ToArray();
        double warmP95 = warmSorted[(int)(warmSorted.Length * 0.95)];
        double jitRatio = warmAvg == 0 ? double.PositiveInfinity : coldAvg / warmAvg;

        _output.WriteLine("Cold vs Warm Analysis (first call vs. subsequent)");
        _output.WriteLine(new string('═', 50));
        _output.WriteLine($"  Files:           {scripts.Count}");
        _output.WriteLine($"  Cold avg:        {coldAvg:F3} ms");
        _output.WriteLine($"  Warm avg:        {warmAvg:F3} ms");
        _output.WriteLine($"  Warm P95:        {warmP95:F3} ms");
        _output.WriteLine($"  JIT overhead:    {jitRatio:F1}x (cold/warm ratio)");
    }

    private static List<Token> FilterParserTokens(List<Token> tokens)
    {
        var result = new List<Token>(tokens.Count);
        foreach (var t in tokens)
        {
            if (t.Type is not (TokenType.SingleLineComment or TokenType.BlockComment or TokenType.Shebang))
            {
                result.Add(t);
            }
        }
        return result;
    }
}
