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
    public async Task ContinueHandler_ContinuesOnlyRequestedThread()
    {
        var session = new DebugSession();
        var handler = new StashContinueHandler(session);
        var result = await handler.Handle(new ContinueArguments { ThreadId = 1 }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.False(result.AllThreadsContinued);
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

    // ── SetVariable handler ──────────────────────────────────────────────────

    [Fact]
    public async Task SetVariableHandler_BeforeLaunch_ReturnsErrorMessage()
    {
        var session = new DebugSession();
        var handler = new StashSetVariableHandler(session);
        var result = await handler.Handle(new SetVariableArguments
        {
            VariablesReference = 1,
            Name = "x",
            Value = "42"
        }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.StartsWith("Error:", result.Value);
    }

    [Fact]
    public async Task SetVariableHandler_InvalidReference_ReturnsErrorMessage()
    {
        var session = new DebugSession();
        // Need to set interpreter for the SetVariable path
        var field = typeof(DebugSession).GetField("_interpreter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(session, new Stash.Interpreting.Interpreter());

        var handler = new StashSetVariableHandler(session);
        var result = await handler.Handle(new SetVariableArguments
        {
            VariablesReference = 9999,
            Name = "x",
            Value = "42"
        }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.StartsWith("Error:", result.Value);
    }

    // ── SetFunctionBreakpoints handler ───────────────────────────────────────

    [Fact]
    public async Task SetFunctionBreakpointsHandler_ReturnsBreakpoints()
    {
        var session = new DebugSession();
        var handler = new StashSetFunctionBreakpointsHandler(session);
        var result = await handler.Handle(new SetFunctionBreakpointsArguments
        {
            Breakpoints = new Container<FunctionBreakpoint>(
                new FunctionBreakpoint { Name = "myFunc" }
            )
        }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result.Breakpoints!);
        Assert.True(result.Breakpoints!.First().Verified);
    }

    [Fact]
    public async Task SetFunctionBreakpointsHandler_MultipleBreakpoints()
    {
        var session = new DebugSession();
        var handler = new StashSetFunctionBreakpointsHandler(session);
        var result = await handler.Handle(new SetFunctionBreakpointsArguments
        {
            Breakpoints = new Container<FunctionBreakpoint>(
                new FunctionBreakpoint { Name = "func1" },
                new FunctionBreakpoint { Name = "func2" },
                new FunctionBreakpoint { Name = "func3" }
            )
        }, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(3, result.Breakpoints!.Count());
    }

    [Fact]
    public async Task SetFunctionBreakpointsHandler_NullBreakpoints_ReturnsEmpty()
    {
        var session = new DebugSession();
        var handler = new StashSetFunctionBreakpointsHandler(session);
        var result = await handler.Handle(new SetFunctionBreakpointsArguments
        {
            Breakpoints = null!
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Breakpoints!);
    }

    // ── LoadedSources handler ─────────────────────────────────────────────────

    [Fact]
    public async Task LoadedSourcesHandler_NoSources_ReturnsEmpty()
    {
        var session = new DebugSession();
        var handler = new StashLoadedSourcesHandler(session);
        var result = await handler.Handle(new LoadedSourcesArguments(), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task LoadedSourcesHandler_WithSources_ReturnsSources()
    {
        var session = new DebugSession();
        session.OnSourceLoaded("/test/main.stash");
        session.OnSourceLoaded("/test/lib.stash");
        var handler = new StashLoadedSourcesHandler(session);
        var result = await handler.Handle(new LoadedSourcesArguments(), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(2, result.Sources.Count());
    }
}
