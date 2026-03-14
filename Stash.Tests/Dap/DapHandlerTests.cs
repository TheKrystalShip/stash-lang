namespace Stash.Tests.Dap;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stash.Dap;
using Stash.Dap.Handlers;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using Xunit;

public class DapHandlerTests
{
    // ── Initialize handler ────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeHandler_ReturnsCorrectCapabilities()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task InitializeHandler_SupportsConfigurationDone()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        Assert.True(result.SupportsConfigurationDoneRequest);
    }

    [Fact]
    public async Task InitializeHandler_SupportsConditionalBreakpoints()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        Assert.True(result.SupportsConditionalBreakpoints);
    }

    [Fact]
    public async Task InitializeHandler_SupportsHitConditionalBreakpoints()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        Assert.True(result.SupportsHitConditionalBreakpoints);
    }

    [Fact]
    public async Task InitializeHandler_SupportsLogPoints()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        Assert.True(result.SupportsLogPoints);
    }

    [Fact]
    public async Task InitializeHandler_SupportsEvaluateForHovers()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        Assert.True(result.SupportsEvaluateForHovers);
    }

    [Fact]
    public async Task InitializeHandler_DoesNotSupportSetVariable()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        Assert.False(result.SupportsSetVariable);
    }

    [Fact]
    public async Task InitializeHandler_DoesNotSupportFunctionBreakpoints()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        Assert.False(result.SupportsFunctionBreakpoints);
    }

    [Fact]
    public async Task InitializeHandler_ExceptionFilters_ContainsAllAndUncaught()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        Assert.NotNull(result.ExceptionBreakpointFilters);
        var filters = result.ExceptionBreakpointFilters!.ToList();
        Assert.Equal(2, filters.Count);
        Assert.Contains(filters, f => f.Filter == "all");
        Assert.Contains(filters, f => f.Filter == "uncaught");
    }

    [Fact]
    public async Task InitializeHandler_ExceptionFilter_All_DefaultIsFalse()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        var allFilter = result.ExceptionBreakpointFilters!.Single(f => f.Filter == "all");
        Assert.False(allFilter.Default);
    }

    [Fact]
    public async Task InitializeHandler_ExceptionFilter_Uncaught_DefaultIsTrue()
    {
        var session = new DebugSession();
        var handler = new StashInitializeHandler(session);
        var result = await handler.Handle(new InitializeRequestArguments { }, CancellationToken.None);
        var uncaughtFilter = result.ExceptionBreakpointFilters!.Single(f => f.Filter == "uncaught");
        Assert.True(uncaughtFilter.Default);
    }

    // ── Threads handler ───────────────────────────────────────────────────────

    [Fact]
    public async Task ThreadsHandler_ReturnsSingleThread()
    {
        var session = new DebugSession();
        var handler = new StashThreadsHandler(session);
        var result = await handler.Handle(new ThreadsArguments { }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result.Threads!);
    }

    [Fact]
    public async Task ThreadsHandler_ThreadIdIsOne()
    {
        var session = new DebugSession();
        var handler = new StashThreadsHandler(session);
        var result = await handler.Handle(new ThreadsArguments { }, CancellationToken.None);
        Assert.Equal(1, result.Threads!.Single().Id);
    }

    [Fact]
    public async Task ThreadsHandler_ThreadNameIsMainThread()
    {
        var session = new DebugSession();
        var handler = new StashThreadsHandler(session);
        var result = await handler.Handle(new ThreadsArguments { }, CancellationToken.None);
        Assert.Equal("Main Thread", result.Threads!.Single().Name);
    }

    // ── SetBreakpoints handler ────────────────────────────────────────────────

    [Fact]
    public async Task SetBreakpointsHandler_ReturnsBreakpointsInResponse()
    {
        var session = new DebugSession();
        var handler = new StashSetBreakpointsHandler(session);
        var result = await handler.Handle(new SetBreakpointsArguments
        {
            Source = new Source { Path = "/test/script.stash" },
            Breakpoints = new Container<SourceBreakpoint>(
                new SourceBreakpoint { Line = 5 },
                new SourceBreakpoint { Line = 10 }
            )
        }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotNull(result.Breakpoints);
        Assert.Equal(2, result.Breakpoints!.Count());
    }

    [Fact]
    public async Task SetBreakpointsHandler_NullSource_ThrowsArgumentException()
    {
        var session = new DebugSession();
        var handler = new StashSetBreakpointsHandler(session);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new SetBreakpointsArguments
        {
            Source = new Source { Path = null },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 1 })
        }, CancellationToken.None));
    }

    [Fact]
    public async Task SetBreakpointsHandler_NullBreakpoints_UsesEmptyContainer()
    {
        var session = new DebugSession();
        var handler = new StashSetBreakpointsHandler(session);
        var result = await handler.Handle(new SetBreakpointsArguments
        {
            Source = new Source { Path = "/test/script.stash" },
            Breakpoints = null
        }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result.Breakpoints!);
    }

    [Fact]
    public async Task SetBreakpointsHandler_WithCondition_BreakpointsHaveCondition()
    {
        var session = new DebugSession();
        var handler = new StashSetBreakpointsHandler(session);
        var result = await handler.Handle(new SetBreakpointsArguments
        {
            Source = new Source { Path = "/test/script.stash" },
            Breakpoints = new Container<SourceBreakpoint>(
                new SourceBreakpoint { Line = 5 },
                new SourceBreakpoint { Line = 10, Condition = "x > 3" }
            )
        }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(2, result.Breakpoints!.Count());
    }

    // ── SetExceptionBreakpoints handler ───────────────────────────────────────

    [Fact]
    public async Task SetExceptionBreakpointsHandler_ReturnsResponse()
    {
        var session = new DebugSession();
        var handler = new StashSetExceptionBreakpointsHandler(session);
        var result = await handler.Handle(new SetExceptionBreakpointsArguments
        {
            Filters = new Container<string>("all")
        }, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SetExceptionBreakpointsHandler_NullFilters_DoesNotThrow()
    {
        var session = new DebugSession();
        var handler = new StashSetExceptionBreakpointsHandler(session);
        var result = await handler.Handle(new SetExceptionBreakpointsArguments
        {
            Filters = null!
        }, CancellationToken.None);
        Assert.NotNull(result);
    }

    // ── Continue handler ──────────────────────────────────────────────────────

    [Fact]
    public async Task ContinueHandler_ReturnsAllThreadsContinued()
    {
        var session = new DebugSession();
        var handler = new StashContinueHandler(session);
        var result = await handler.Handle(new ContinueArguments { ThreadId = 1 }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.True(result.AllThreadsContinued);
    }

    // ── Stepping handlers ─────────────────────────────────────────────────────

    [Fact]
    public async Task NextHandler_ReturnsResponse()
    {
        var session = new DebugSession();
        var handler = new StashNextHandler(session);
        var result = await handler.Handle(new NextArguments { ThreadId = 1 }, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task StepInHandler_ReturnsResponse()
    {
        var session = new DebugSession();
        var handler = new StashStepInHandler(session);
        var result = await handler.Handle(new StepInArguments { ThreadId = 1 }, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task StepOutHandler_ReturnsResponse()
    {
        var session = new DebugSession();
        var handler = new StashStepOutHandler(session);
        var result = await handler.Handle(new StepOutArguments { ThreadId = 1 }, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task PauseHandler_ReturnsResponse()
    {
        var session = new DebugSession();
        var handler = new StashPauseHandler(session);
        var result = await handler.Handle(new PauseArguments { ThreadId = 1 }, CancellationToken.None);
        Assert.NotNull(result);
    }

    // ── ConfigurationDone handler ─────────────────────────────────────────────

    [Fact]
    public async Task ConfigurationDoneHandler_ReturnsResponse()
    {
        var session = new DebugSession();
        var handler = new StashConfigurationDoneHandler(session);
        var result = await handler.Handle(new ConfigurationDoneArguments { }, CancellationToken.None);
        Assert.NotNull(result);
    }

    // ── Disconnect handler ────────────────────────────────────────────────────

    [Fact]
    public async Task DisconnectHandler_ReturnsResponse()
    {
        var session = new DebugSession();
        var handler = new StashDisconnectHandler(session);
        var result = await handler.Handle(new DisconnectArguments { }, CancellationToken.None);
        Assert.NotNull(result);
    }

    // ── StackTrace handler ────────────────────────────────────────────────────

    [Fact]
    public async Task StackTraceHandler_ReturnsEmptyFramesBeforeLaunch()
    {
        var session = new DebugSession();
        var handler = new StashStackTraceHandler(session);
        var result = await handler.Handle(new StackTraceArguments { ThreadId = 1 }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotNull(result.StackFrames);
        Assert.Empty(result.StackFrames!);
    }

    [Fact]
    public async Task StackTraceHandler_TotalFramesMatchesFrameCount()
    {
        var session = new DebugSession();
        var handler = new StashStackTraceHandler(session);
        var result = await handler.Handle(new StackTraceArguments { ThreadId = 1 }, CancellationToken.None);
        Assert.Equal(result.StackFrames!.Count(), result.TotalFrames);
    }

    // ── Scopes handler ────────────────────────────────────────────────────────

    [Fact]
    public async Task ScopesHandler_ReturnsEmptyScopesBeforeLaunch()
    {
        var session = new DebugSession();
        var handler = new StashScopesHandler(session);
        var result = await handler.Handle(new ScopesArguments { FrameId = 0 }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotNull(result.Scopes);
        Assert.Empty(result.Scopes!);
    }

    // ── Variables handler ─────────────────────────────────────────────────────

    [Fact]
    public async Task VariablesHandler_ReturnsEmptyForInvalidReference()
    {
        var session = new DebugSession();
        var handler = new StashVariablesHandler(session);
        var result = await handler.Handle(new VariablesArguments { VariablesReference = 999 }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotNull(result.Variables);
        Assert.Empty(result.Variables!);
    }

    // ── Evaluate handler ──────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateHandler_BeforeLaunch_ReturnsNoInterpreter()
    {
        var session = new DebugSession();
        var handler = new StashEvaluateHandler(session);
        var result = await handler.Handle(new EvaluateArguments { Expression = "1 + 2" }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("No interpreter", result.Result);
    }

    [Fact]
    public async Task EvaluateHandler_CatchesExceptions_ReturnsNonNullResult()
    {
        var session = new DebugSession();
        var handler = new StashEvaluateHandler(session);
        var result = await handler.Handle(new EvaluateArguments { Expression = "some_undefined_expression" }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotNull(result.Result);
        Assert.NotEmpty(result.Result);
    }

    [Fact]
    public async Task EvaluateHandler_VariablesReferenceIsZero_BeforeLaunch()
    {
        var session = new DebugSession();
        var handler = new StashEvaluateHandler(session);
        var result = await handler.Handle(new EvaluateArguments { Expression = "x" }, CancellationToken.None);
        Assert.Equal(0, result.VariablesReference);
    }
}
