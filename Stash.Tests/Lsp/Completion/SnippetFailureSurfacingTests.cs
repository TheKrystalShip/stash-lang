namespace Stash.Tests.Lsp.Completion;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Completion.Snippets;
using Xunit;

/// <summary>
/// Tests for the P3 failure-surfacing contract: invalid snippets must produce
/// per-error log entries plus a single summary <c>window/showMessage</c> popup,
/// and the LSP must never refuse to start when validation fails.
/// </summary>
public class SnippetFailureSurfacingTests
{
    // ── Reporter contract ────────────────────────────────────────────────────

    [Fact]
    public void Report_NoErrors_FiresNeitherLogNorWindow()
    {
        var logger = new CapturingLogger();
        var windowCalls = new List<(MessageType Type, string Message)>();
        var reporter = new SnippetDiagnosticsReporter(logger);

        reporter.Report((t, m) => windowCalls.Add((t, m)), Array.Empty<SnippetLoadError>(), "bundled");

        Assert.Empty(logger.Entries);
        Assert.Empty(windowCalls);
    }

    [Fact]
    public void Report_OneError_FiresOneLogAndOneWindowMessage()
    {
        var logger = new CapturingLogger();
        var windowCalls = new List<(MessageType Type, string Message)>();
        var reporter = new SnippetDiagnosticsReporter(logger);

        var errors = new[]
        {
            new SnippetLoadError("bundled:fori:Any", "bundled", "body failed to parse"),
        };

        reporter.Report((t, m) => windowCalls.Add((t, m)), errors, "bundled snippets");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Contains("bundled:fori:Any", logger.Entries[0].Message);
        Assert.Contains("body failed to parse", logger.Entries[0].Message);

        Assert.Single(windowCalls);
        Assert.Equal(MessageType.Error, windowCalls[0].Type);
        Assert.Contains("1 invalid snippet", windowCalls[0].Message);
        Assert.Contains("bundled snippets", windowCalls[0].Message);
    }

    [Fact]
    public void Report_MultipleErrors_FiresOneLogPerErrorAndExactlyOneWindowMessage()
    {
        var logger = new CapturingLogger();
        var windowCalls = new List<(MessageType Type, string Message)>();
        var reporter = new SnippetDiagnosticsReporter(logger);

        var errors = new[]
        {
            new SnippetLoadError("bundled:bad1:Any", "bundled", "prefix is empty"),
            new SnippetLoadError("bundled:bad2:Any", "bundled", "body is empty"),
            new SnippetLoadError("bundled:bad3:Any", "bundled", "tabstop syntax invalid"),
        };

        reporter.Report((t, m) => windowCalls.Add((t, m)), errors, "bundled snippets");

        // One log per error: each LoadErrors entry must surface (the LOAD-ERRORS CONTRACT).
        Assert.Equal(3, logger.Entries.Count);
        Assert.Contains(logger.Entries, e => e.Message.Contains("bundled:bad1:Any"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("bundled:bad2:Any"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("bundled:bad3:Any"));

        // Exactly one window message (summary), to avoid spamming users with N popups.
        Assert.Single(windowCalls);
        Assert.Equal(MessageType.Error, windowCalls[0].Type);
        Assert.Contains("3 invalid snippets", windowCalls[0].Message);
        Assert.Contains("bundled snippets", windowCalls[0].Message);
    }

    [Fact]
    public void Report_NullShowMessage_StillFiresLogEntries()
    {
        // The LSP-stays-up guarantee: if the window facade is unavailable, the log path
        // still fires so the diagnostics aren't lost.
        var logger = new CapturingLogger();
        var reporter = new SnippetDiagnosticsReporter(logger);

        var errors = new[]
        {
            new SnippetLoadError("bundled:x:Any", "bundled", "some reason"),
        };

        reporter.Report(showMessage: null, errors, "bundled");

        Assert.Single(logger.Entries);
    }

    // ── Failure isolation — the load-bearing constraint ──────────────────────

    [Fact]
    public void Report_LoggerThrows_DoesNotPropagate()
    {
        // LSP-stays-up guarantee: a logger that throws must not crash the startup pipeline.
        var windowCalls = new List<(MessageType, string)>();
        var reporter = new SnippetDiagnosticsReporter(new ThrowingLogger());

        var errors = new[] { new SnippetLoadError("x", "y", "z") };

        var ex = Record.Exception(() => reporter.Report((t, m) => windowCalls.Add((t, m)), errors, "bundled"));
        Assert.Null(ex);
    }

    [Fact]
    public void Report_ShowMessageCallbackThrows_DoesNotPropagate()
    {
        // LSP-stays-up guarantee: a misbehaving callback must not crash the startup pipeline.
        var logger = new CapturingLogger();
        var reporter = new SnippetDiagnosticsReporter(logger);

        var errors = new[] { new SnippetLoadError("x", "y", "z") };

        var ex = Record.Exception(() => reporter.Report(
            (t, m) => throw new InvalidOperationException("window boom"),
            errors,
            "bundled"));
        Assert.Null(ex);
        // Log entry still fired despite callback failure.
        Assert.Single(logger.Entries);
    }

    // ── End-to-end: BundledSnippetRegistry continues serving valid snippets ──

    [Fact]
    public void BundledRegistry_NeverThrows_OnConstruction()
    {
        // Smoke test: instantiating the production registry must not throw under any
        // circumstance. The bundled.json is shipped valid; this asserts the contract holds.
        var ex = Record.Exception(() => new BundledSnippetRegistry());
        Assert.Null(ex);
    }

    [Fact]
    public void BundledRegistry_ExposesLoadErrors_AsTheSingleSurfaceForFailures()
    {
        // Contract: LoadErrors is the canonical accessor the reporter reads from.
        // For the production bundled set, it should be empty (every shipped snippet is valid).
        var registry = new BundledSnippetRegistry();

        Assert.Empty(registry.LoadErrors);
        Assert.NotEmpty(registry.Snapshot());
    }

    // ── Test doubles ─────────────────────────────────────────────────────────

    private sealed class CapturingLogger : ILogger<SnippetDiagnosticsReporter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class ThrowingLogger : ILogger<SnippetDiagnosticsReporter>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel l, EventId e, TState s, Exception? ex, Func<TState, Exception?, string> f)
            => throw new InvalidOperationException("logger boom");
    }
}
