using System;
using System.Collections.Generic;
using System.Reflection;
using Stash.Common;
using Stash.Interpreting;
using Stash.Runtime;
using Xunit;
using StashExecutionContext = Stash.Interpreting.ExecutionContext;

namespace Stash.Tests.Interpreting;

public class CommandHelpersTests
{
    private static readonly SourceSpan _testSpan = new("test", 1, 1, 1, 1);

    private static Interpreter CreateInterpreter() => new();

    // ── RunCaptured ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_EchoCommand_CapturesStdout()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();
        var (stdout, stderr, exitCode) = interpreter.RunCaptured("echo", ["hello"], null, _testSpan);

        Assert.Contains("hello", stdout);
        Assert.Equal("", stderr);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_WithStdin_PassesInputToProcess()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();
        var (stdout, _, _) = interpreter.RunCaptured("cat", [], "test input", _testSpan);

        Assert.Contains("test input", stdout);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_NullStdin_DoesNotRedirectInput()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();
        var (stdout, _, exitCode) = interpreter.RunCaptured("echo", ["test"], null, _testSpan);

        Assert.Contains("test", stdout);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_NonZeroExitCode_ReturnsExitCode()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();
        var (_, _, exitCode) = interpreter.RunCaptured("sh", ["-c", "exit 42"], null, _testSpan);

        Assert.Equal(42, exitCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_StderrOutput_CapturesStderr()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();
        var (_, stderr, _) = interpreter.RunCaptured("sh", ["-c", "echo error >&2"], null, _testSpan);

        Assert.Contains("error", stderr);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_InvalidProgram_ThrowsRuntimeError()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();

        Assert.Throws<RuntimeError>(() =>
            interpreter.RunCaptured("__nonexistent_program_xyz__", [], null, _testSpan));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_WithArguments_PassesArgsCorrectly()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();
        var (stdout, _, exitCode) = interpreter.RunCaptured("echo", ["-n", "hello"], null, _testSpan);

        Assert.Equal(0, exitCode);
        Assert.Contains("hello", stdout);
    }

    // ── RunPassthrough ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public void RunPassthrough_SimpleCommand_ReturnsExitCode()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();
        var (stdout, stderr, exitCode) = interpreter.RunPassthrough("true", [], _testSpan);

        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunPassthrough_FailingCommand_ReturnsNonZeroExitCode()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();
        var (_, _, exitCode) = interpreter.RunPassthrough("false", [], _testSpan);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunPassthrough_StdoutStderrAlwaysEmpty()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();
        var (stdout, stderr, _) = interpreter.RunPassthrough("echo", ["hello"], _testSpan);

        Assert.Equal("", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunPassthrough_InvalidProgram_ThrowsRuntimeError()
    {
        if (OperatingSystem.IsWindows()) { return; }

        var interpreter = CreateInterpreter();

        Assert.Throws<RuntimeError>(() =>
            interpreter.RunPassthrough("__nonexistent_program_xyz__", [], _testSpan));
    }

    // ── ExecutionContext elevation properties ─────────────────────────────────

    [Fact]
    public void ExecutionContext_ElevationActive_DefaultsFalse()
    {
        var env = new Stash.Interpreting.Environment();
        var ctx = new StashExecutionContext(env);

        Assert.False(ctx.ElevationActive);
    }

    [Fact]
    public void ExecutionContext_ElevationCommand_DefaultsNull()
    {
        var env = new Stash.Interpreting.Environment();
        var ctx = new StashExecutionContext(env);

        Assert.Null(ctx.ElevationCommand);
    }

    [Fact]
    public void ExecutionContext_ElevationProperties_Settable()
    {
        var env = new Stash.Interpreting.Environment();
        var ctx = new StashExecutionContext(env)
        {
            ElevationActive = true,
            ElevationCommand = "sudo"
        };

        Assert.True(ctx.ElevationActive);
        Assert.Equal("sudo", ctx.ElevationCommand);
    }

    // ── Fork propagation ──────────────────────────────────────────────────────

    private static StashExecutionContext GetCtx(Interpreter interpreter)
    {
        FieldInfo field = typeof(Interpreter).GetField("Ctx", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Field 'Ctx' not found on Interpreter");
        return (StashExecutionContext)(field.GetValue(interpreter)
            ?? throw new InvalidOperationException("Field 'Ctx' is null on Interpreter"));
    }

    [Fact]
    public void Fork_PropagatesElevationActive()
    {
        var interpreter = CreateInterpreter();
        var ctx = GetCtx(interpreter);
        ctx.ElevationActive = true;

        var childEnv = new Stash.Interpreting.Environment();
        var child = interpreter.Fork(childEnv);
        var childCtx = GetCtx(child);

        Assert.True(childCtx.ElevationActive);
    }

    [Fact]
    public void Fork_PropagatesElevationCommand()
    {
        var interpreter = CreateInterpreter();
        var ctx = GetCtx(interpreter);
        ctx.ElevationCommand = "sudo";

        var childEnv = new Stash.Interpreting.Environment();
        var child = interpreter.Fork(childEnv);
        var childCtx = GetCtx(child);

        Assert.Equal("sudo", childCtx.ElevationCommand);
    }

    [Fact]
    public void Fork_ElevationNotActive_ChildAlsoNotActive()
    {
        var interpreter = CreateInterpreter();
        // elevation properties left at defaults

        var childEnv = new Stash.Interpreting.Environment();
        var child = interpreter.Fork(childEnv);
        var childCtx = GetCtx(child);

        Assert.False(childCtx.ElevationActive);
        Assert.Null(childCtx.ElevationCommand);
    }
}
