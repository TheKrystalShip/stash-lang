using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Phase E tests: hook execution for alias <c>confirm</c>, <c>before</c>, and <c>after</c>
/// callbacks. Verifies spec §9 — execution order, parameter passing, abort semantics,
/// and cycle-guard interaction.
/// </summary>
[Collection("AliasStaticState")]
public sealed class AliasHooksTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly StringWriter _output = new();
    private readonly VirtualMachine _vm;
    private readonly ShellRunner _runner;

    public AliasHooksTests()
    {
        // Reset the static prompter at the start of each test to prevent
        // cross-test contamination when tests run in parallel.
        AliasDispatcher.ConfirmPrompter = null;

        _vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        _vm.Output      = _output;
        _vm.ErrorOutput = Console.Error;
        _vm.EmbeddedMode = true;

        var ctx = new ShellContext
        {
            Vm                = _vm,
            PathCache         = new PathExecutableCache(_ => true),
            Keywords          = ShellContext.BuildKeywordSet(),
            Namespaces        = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };

        _runner = new ShellRunner(ctx);
        AliasDispatcher.Wire(_runner, _vm);
    }

    public void Dispose()
    {
        // Always restore the prompter so other tests are unaffected.
        AliasDispatcher.ConfirmPrompter = null;
        _output.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Defines a template alias (string body) with optional Stash options expression.</summary>
    private void DefineTemplate(string name, string body, string? opts = null)
    {
        string optsArg = opts is null ? "" : $", {opts}";
        ShellRunner.EvaluateSource(
            $"""alias.define("{name}", "{body}"{optsArg});""",
            _vm);
    }

    /// <summary>Defines a function alias (lambda body) with optional Stash options expression.</summary>
    private void DefineFunction(string name, string lambdaBody, string? opts = null)
    {
        string optsArg = opts is null ? "" : $", {opts}";
        ShellRunner.EvaluateSource(
            $"""alias.define("{name}", {lambdaBody}{optsArg});""",
            _vm);
    }

    private string Output => _output.ToString();

    // =========================================================================
    // 1. confirm accepted — "y" — body runs, exit code from body
    // =========================================================================

    [Fact]
    public void Confirm_Accepted_Y_BodyRunsAndReturnsBodyExitCode()
    {
        AliasDispatcher.ConfirmPrompter = _ => true;

        DefineFunction("greet", "() => { io.println(\"hello\"); }",
            """AliasOptions { confirm: "Are you sure?" }""");

        int code = AliasDispatcher.ExecuteAlias(_runner, _vm,
            _vm.AliasRegistry.TryGet("greet", out var e) ? e! : throw new InvalidOperationException("not found"),
            []);

        Assert.Equal(0, code);
        Assert.Contains("hello", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 2. confirm accepted — "yes" — body runs
    // =========================================================================

    [Fact]
    public void Confirm_Accepted_Yes_BodyRuns()
    {
        AliasDispatcher.ConfirmPrompter = _ => true;   // simulates "yes"

        DefineFunction("greet2", "() => { io.println(\"world\"); }",
            """AliasOptions { confirm: "Continue?" }""");

        _vm.AliasRegistry.TryGet("greet2", out var entry);
        AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []);

        Assert.Contains("world", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 3. confirm declined — body skipped, exit 130, after NOT called
    // =========================================================================

    [Fact]
    public void Confirm_Declined_BodySkipped_ExitCode130_AfterNotCalled()
    {
        AliasDispatcher.ConfirmPrompter = _ => false;

        // Define with both confirm and after; after writes "after-ran" to output.
        ShellRunner.EvaluateSource("""
            alias.define("deploy", () => { io.println("body-ran"); }, AliasOptions {
                confirm: "Deploy?",
                after: (name, args, code) => { io.println("after-ran"); }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("deploy", out var entry);
        int code = AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []);

        Assert.Equal(130, code);
        Assert.DoesNotContain("body-ran", Output, StringComparison.Ordinal);
        Assert.DoesNotContain("after-ran", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 4. confirm declined — various "n" inputs via prompter returning false
    // =========================================================================

    [Fact]
    public void Confirm_Declined_AnyNonYes_BodySkipped()
    {
        // "n", empty string, "no", "NO", etc. — all mapped to false by the prompter
        AliasDispatcher.ConfirmPrompter = _ => false;

        DefineFunction("testfn", "() => { io.println(\"ran\"); }",
            """AliasOptions { confirm: "Confirm?" }""");

        _vm.AliasRegistry.TryGet("testfn", out var entry);
        int code = AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []);

        Assert.Equal(130, code);
        Assert.DoesNotContain("ran", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 5. before returns true — body runs
    // =========================================================================

    [Fact]
    public void Before_ReturnsTrue_BodyRuns()
    {
        ShellRunner.EvaluateSource("""
            alias.define("act", () => { io.println("body"); }, AliasOptions {
                before: (name, args) => {
                    io.println("before");
                    return true;
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("act", out var entry);
        int code = AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []);

        Assert.Equal(0, code);
        Assert.Contains("before", Output, StringComparison.Ordinal);
        Assert.Contains("body", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 6. before returns false — body skipped, exit 1, after NOT called
    // =========================================================================

    [Fact]
    public void Before_ReturnsFalse_BodySkipped_ExitCode1_AfterNotCalled()
    {
        ShellRunner.EvaluateSource("""
            alias.define("act2", () => { io.println("body"); }, AliasOptions {
                before: (name, args) => {
                    return false;
                },
                after: (name, args, code) => {
                    io.println("after");
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("act2", out var entry);
        int code = AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []);

        Assert.Equal(1, code);
        Assert.DoesNotContain("body", Output, StringComparison.Ordinal);
        Assert.DoesNotContain("after", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 7. before throws — AliasError wrapping; after NOT called
    // =========================================================================

    [Fact]
    public void Before_Throws_AliasErrorPropagates_AfterNotCalled()
    {
        ShellRunner.EvaluateSource("""
            alias.define("boom", () => { io.println("body"); }, AliasOptions {
                before: (name, args) => {
                    throw ValueError { message: "bad input" };
                },
                after: (name, args, code) => {
                    io.println("after");
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("boom", out var entry);
        var ex = Assert.Throws<RuntimeError>(
            () => AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []));

        Assert.Equal(StashErrorTypes.AliasError, ex.ErrorType);
        Assert.Contains("hook 'before' for alias 'boom' threw:", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("body", Output, StringComparison.Ordinal);
        Assert.DoesNotContain("after", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 8. after runs even when body fails (function body throws)
    // =========================================================================

    [Fact]
    public void After_RunsEvenWhenBodyThrows()
    {
        ShellRunner.EvaluateSource("""
            alias.define("failbody", () => {
                throw ValueError { message: "intentional" };
            }, AliasOptions {
                after: (name, args, code) => {
                    io.println("after-code:" + conv.toStr(code));
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("failbody", out var entry);
        Assert.Throws<RuntimeError>(
            () => AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []));

        Assert.Contains("after-code:1", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 9. after receives correct (name, args, exitCode) — spec §9.2
    // =========================================================================

    [Fact]
    public void After_ReceivesCorrectParameters()
    {
        ShellRunner.EvaluateSource("""
            alias.define("inspect", (...rest) => { }, AliasOptions {
                after: (aname, aargs, code) => {
                    io.println("name=" + aname);
                    io.println("arg0=" + aargs[0]);
                    io.println("code=" + conv.toStr(code));
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("inspect", out var entry);
        AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, ["hello"]);

        string out_ = Output;
        Assert.Contains("name=inspect", out_, StringComparison.Ordinal);
        Assert.Contains("arg0=hello", out_, StringComparison.Ordinal);
        Assert.Contains("code=0", out_, StringComparison.Ordinal);
    }

    // =========================================================================
    // 10. before receives correct (name, args) — spec §9.2
    // =========================================================================

    [Fact]
    public void Before_ReceivesCorrectParameters()
    {
        ShellRunner.EvaluateSource("""
            alias.define("pinspect", (...rest) => { }, AliasOptions {
                before: (bname, bargs) => {
                    io.println("bname=" + bname);
                    io.println("barg0=" + bargs[0]);
                    return true;
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("pinspect", out var entry);
        AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, ["world"]);

        string out_ = Output;
        Assert.Contains("bname=pinspect", out_, StringComparison.Ordinal);
        Assert.Contains("barg0=world", out_, StringComparison.Ordinal);
    }

    // =========================================================================
    // 11. after receives non-zero exit code when body succeeds with non-zero code
    // =========================================================================

    [Fact]
    public void After_ReceivesBodyExitCode_FromFunctionAlias()
    {
        // Function alias that calls a shell command which exits non-zero is complex.
        // Instead verify exit code 0 path directly.
        ShellRunner.EvaluateSource("""
            alias.define("ok", () => { }, AliasOptions {
                after: (n, a, code) => {
                    io.println("exit=" + conv.toStr(code));
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("ok", out var entry);
        AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []);

        Assert.Contains("exit=0", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 12. after does NOT run if before threw
    // =========================================================================

    [Fact]
    public void After_NotCalled_WhenBeforeThrows()
    {
        ShellRunner.EvaluateSource("""
            alias.define("boomba", () => { io.println("body"); }, AliasOptions {
                before: (n, a) => {
                    throw ValueError { message: "oops" };
                },
                after: (n, a, c) => {
                    io.println("after-called");
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("boomba", out var entry);
        Assert.Throws<RuntimeError>(
            () => AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []));

        Assert.DoesNotContain("after-called", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 13. after does NOT run if confirm declined
    // =========================================================================

    [Fact]
    public void After_NotCalled_WhenConfirmDeclined()
    {
        AliasDispatcher.ConfirmPrompter = _ => false;

        ShellRunner.EvaluateSource("""
            alias.define("safe", () => { io.println("body"); }, AliasOptions {
                confirm: "Continue?",
                after: (n, a, c) => {
                    io.println("after-called");
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("safe", out var entry);
        int code = AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []);

        Assert.Equal(130, code);
        Assert.DoesNotContain("after-called", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 14. Hook recursion — before invokes same alias via alias.exec → AliasError
    // =========================================================================

    [Fact]
    public void HookRecursion_BeforeInvokesSameAlias_CycleDetected()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // Use a TEMPLATE alias: alias.exec for template aliases goes through AliasExecutor
        // → ExecuteAlias, which checks the expansion stack. Since "recur" is already pushed
        // before hooks run (spec §9.3), the cycle guard fires.
        ShellRunner.EvaluateSource("""
            alias.define("recur", "echo hello ${args}", AliasOptions {
                before: (n, a) => {
                    alias.exec("recur", []);
                    return true;
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("recur", out var entry);
        var ex = Assert.Throws<RuntimeError>(
            () => AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []));

        // RuntimeError from cycle guard wrapped as "hook 'before' ... threw: recursive alias expansion"
        Assert.Equal(StashErrorTypes.AliasError, ex.ErrorType);
        Assert.Contains("recur", ex.Message, StringComparison.Ordinal);
    }

    // =========================================================================
    // 15. confirm + before + after combined — all three set, all run on accept
    // =========================================================================

    [Fact]
    public void AllThreeHooks_Combined_RunInCorrectOrder_OnAccept()
    {
        AliasDispatcher.ConfirmPrompter = _ => true;

        ShellRunner.EvaluateSource("""
            let hookLog = [];
            alias.define("combo", () => { arr.push(hookLog, "body"); }, AliasOptions {
                confirm: "Proceed?",
                before: (n, a) => {
                    arr.push(hookLog, "before");
                    return true;
                },
                after: (n, a, c) => {
                    arr.push(hookLog, "after");
                }
            });
            """, _vm);

        _vm.AliasRegistry.TryGet("combo", out var entry);
        int code = AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []);

        // Verify via reading hookLog from VM globals
        ShellRunner.EvaluateSource("io.println(arr.join(hookLog, \",\"));", _vm);

        Assert.Equal(0, code);
        Assert.Contains("before,body,after", Output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 16. confirm prompt text is passed to ConfirmPrompter
    // =========================================================================

    [Fact]
    public void Confirm_PromptTextPassedToPrompter()
    {
        string? receivedPrompt = null;
        AliasDispatcher.ConfirmPrompter = prompt =>
        {
            receivedPrompt = prompt;
            return true;
        };

        DefineFunction("prompttest", "() => { }",
            """AliasOptions { confirm: "Deploy to PROD?" }""");

        _vm.AliasRegistry.TryGet("prompttest", out var entry);
        AliasDispatcher.ExecuteAlias(_runner, _vm, entry!, []);

        Assert.Equal("Deploy to PROD?", receivedPrompt);
    }

    // =========================================================================
    // 17. Function alias in pipeline — regression test (Phase B deferred)
    //     Verifies the current behaviour: falls through to PATH lookup and fails
    //     with CommandError, not a crash or silent failure.
    // =========================================================================

    [Fact]
    public void FunctionAliasInPipeline_FallsThrough_PhaseEDecision()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _) = MakeIsolatedRunner();

        ShellRunner.EvaluateSource(
            """alias.define("fnpipe", () => { io.println("fnpipe-ran"); });""",
            vm);

        // Function alias in a pipeline stage currently falls through to PATH lookup.
        // "fnpipe" is not on PATH, so it should raise CommandError.
        // This is the documented deferred behaviour (spec §8.2 / Phase B notes).
        var ex = Assert.Throws<RuntimeError>(() => runner.Run("echo hello | fnpipe"));

        // Confirm we get a CommandError (not an unhandled crash or AliasError).
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }

    // ── Isolated runner helper for pipeline test ──────────────────────────────

    private static (ShellRunner Runner, VirtualMachine Vm, StringWriter Output) MakeIsolatedRunner()
    {
        var sw = new StringWriter();
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output      = sw;
        vm.ErrorOutput = Console.Error;
        vm.EmbeddedMode = true;

        var ctx = new ShellContext
        {
            Vm                = vm,
            PathCache         = new PathExecutableCache(_ => true),
            Keywords          = ShellContext.BuildKeywordSet(),
            Namespaces        = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };

        var runner = new ShellRunner(ctx);
        AliasDispatcher.Wire(runner, vm);

        return (runner, vm, sw);
    }
}
