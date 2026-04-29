using System.Collections.Generic;
using Stash.Runtime;
using Stash.Stdlib.BuiltIns;

namespace Stash.Tests.Stdlib;

[CollectionDefinition("PromptTests", DisableParallelization = true)]
public sealed class PromptTestsCollection { }

/// <summary>
/// Unit tests for the <c>prompt</c> namespace built-in functions in
/// <see cref="PromptBuiltIns"/>. Each test resets all static state via
/// <c>ResetAllForTesting()</c> so tests are order-independent.
/// </summary>
[Collection("PromptTests")]
public class PromptBuiltInsTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // 1. prompt.set / prompt.render
    // =========================================================================

    [Fact]
    public void Set_WithValidOneParmFn_RegistersCallable_RenderReturnsItsString()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            prompt.set((ctx) => { return "hello> "; });
            let result = prompt.render();
            """);
        Assert.Equal("hello> ", result);
    }

    [Fact]
    public void Set_WithVariadicFn_Accepted()
    {
        PromptBuiltIns.ResetAllForTesting();
        // Variadic fn (Arity == -1) must also be accepted
        var result = Run("""
            prompt.set((...args) => { return "variadic> "; });
            let result = prompt.render();
            """);
        Assert.Equal("variadic> ", result);
    }

    [Fact]
    public void Set_NonCallable_ThrowsTypeError()
    {
        PromptBuiltIns.ResetAllForTesting();
        var ex = RunCapturingError("prompt.set(5);");
        Assert.Equal(StashErrorTypes.TypeError, ex.ErrorType);
    }

    [Fact]
    public void Set_ZeroArgFn_ThrowsTypeError()
    {
        PromptBuiltIns.ResetAllForTesting();
        // fn () { } has Arity=0, which fails MinArity<=1<=Arity
        var ex = RunCapturingError("""prompt.set(() => { return "x"; });""");
        Assert.Equal(StashErrorTypes.TypeError, ex.ErrorType);
    }

    [Fact]
    public void Set_ThreeArgFn_ThrowsTypeError()
    {
        PromptBuiltIns.ResetAllForTesting();
        // fn (a, b, c) { } has Arity=3, MinArity=3 => MinArity(3)<=1 is false
        var ex = RunCapturingError("""prompt.set((a, b, c) => { return ""; });""");
        Assert.Equal(StashErrorTypes.TypeError, ex.ErrorType);
    }

    // =========================================================================
    // 2. prompt.reset
    // =========================================================================

    [Fact]
    public void Reset_AfterSet_ClearsRegisteredFn_RenderFallsBackToDefault()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            prompt.set((ctx) => { return "custom> "; });
            prompt.reset();
            let result = prompt.render();
            """);
        Assert.Equal("stash> ", result);
    }

    [Fact]
    public void Render_WithNoFnRegistered_ReturnsDefaultPrompt()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("let result = prompt.render();");
        Assert.Equal("stash> ", result);
    }

    // =========================================================================
    // 3. prompt.context
    // =========================================================================

    [Fact]
    public void Context_HasCwdField_NonEmpty()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            let ctx = prompt.context();
            let result = ctx.cwd;
            """);
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void Context_HasUserField_NonEmpty()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            let ctx = prompt.context();
            let result = ctx.user;
            """);
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void Context_HasHostField_NonEmpty()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            let ctx = prompt.context();
            let result = ctx.host;
            """);
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void Context_HasModeField()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            let ctx = prompt.context();
            let result = ctx.mode;
            """);
        Assert.IsType<string>(result);
        // mode is "stash" (not in shell mode in tests)
        Assert.Equal("stash", result);
    }

    [Fact]
    public void Context_LineNumber_IncreasesOnEachCall()
    {
        PromptBuiltIns.ResetAllForTesting();
        // After reset, _lineNumber = 0. First call returns 1, second returns 2.
        var result = Run("""
            let ctx1 = prompt.context();
            let ctx2 = prompt.context();
            let result = ctx2.lineNumber - ctx1.lineNumber;
            """);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Context_FirstCallAfterReset_LineNumberIsOne()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            let ctx = prompt.context();
            let result = ctx.lineNumber;
            """);
        Assert.Equal(1L, result);
    }

    // =========================================================================
    // 4. prompt.palette / prompt.setPalette
    // =========================================================================

    [Fact]
    public void Palette_InitiallyNull()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("let result = prompt.palette();");
        Assert.Null(result);
    }

    [Fact]
    public void SetPalette_StoresPalette_RetrievableViaPalette()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            prompt.setPalette({fg: "red"});
            let p = prompt.palette();
            let result = p.fg;
            """);
        Assert.Equal("red", result);
    }

    // =========================================================================
    // 5. prompt.bootstrapDir
    // =========================================================================

    [Fact]
    public void BootstrapDir_ReturnsNonEmptyStringEndingWithPrompt()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("let result = prompt.bootstrapDir();");
        Assert.IsType<string>(result);
        string dir = (string)result!;
        Assert.NotEmpty(dir);
        Assert.True(dir.EndsWith("prompt") || dir.EndsWith("prompt/") || dir.EndsWith("prompt\\"),
            $"Expected dir to end with 'prompt', was: {dir}");
    }

    // =========================================================================
    // 6. prompt.themeRegister / prompt.themeUse / prompt.themeCurrent / prompt.themeList
    // =========================================================================

    [Fact]
    public void ThemeRegister_ThenThemeUse_SetsCurrentTheme()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            prompt.themeRegister("myTheme", {fg: "blue"});
            prompt.themeUse("myTheme");
            let result = prompt.themeCurrent();
            """);
        Assert.Equal("myTheme", result);
    }

    [Fact]
    public void ThemeUse_SetsActivePalette()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            prompt.themeRegister("myTheme", {fg: "blue"});
            prompt.themeUse("myTheme");
            let p = prompt.palette();
            let result = p.fg;
            """);
        Assert.Equal("blue", result);
    }

    [Fact]
    public void ThemeUse_UnknownTheme_ThrowsValueError()
    {
        PromptBuiltIns.ResetAllForTesting();
        var ex = RunCapturingError("""prompt.themeUse("nonexistent");""");
        Assert.Equal(StashErrorTypes.ValueError, ex.ErrorType);
    }

    [Fact]
    public void ThemeList_AfterRegister_ReturnsSortedNames()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            prompt.themeRegister("b", {});
            prompt.themeRegister("a", {});
            let result = prompt.themeList();
            """);
        var list = result as List<object?>;
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("b", list[1]);
    }

    [Fact]
    public void ThemeCurrent_BeforeAnyThemeUse_ReturnsEmptyString()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("let result = prompt.themeCurrent();");
        Assert.Equal("", result);
    }

    // =========================================================================
    // 7. prompt.registerStarter / prompt.useStarter / prompt.listStarters
    // =========================================================================

    [Fact]
    public void RegisterStarter_ThenUseStarter_RenderReturnsStarterResult()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            prompt.registerStarter("myStarter", (ctx) => { return ">> "; });
            prompt.useStarter("myStarter");
            let result = prompt.render();
            """);
        Assert.Equal(">> ", result);
    }

    [Fact]
    public void UseStarter_UnknownStarter_ThrowsValueError()
    {
        PromptBuiltIns.ResetAllForTesting();
        var ex = RunCapturingError("""prompt.useStarter("nonexistent");""");
        Assert.Equal(StashErrorTypes.ValueError, ex.ErrorType);
    }

    [Fact]
    public void ListStarters_AfterRegister_ReturnsSortedNames()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("""
            prompt.registerStarter("z", (ctx) => { return "z"; });
            prompt.registerStarter("a", (ctx) => { return "a"; });
            let result = prompt.listStarters();
            """);
        var list = result as List<object?>;
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("z", list[1]);
    }

    [Fact]
    public void ListStarters_NoStarters_ReturnsEmptyList()
    {
        PromptBuiltIns.ResetAllForTesting();
        var result = Run("let result = prompt.listStarters();");
        var list = result as List<object?>;
        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    // =========================================================================
    // 8. prompt.setContinuation / prompt.resetContinuation
    // =========================================================================

    [Fact]
    public void SetContinuation_WithValidFn_Accepted()
    {
        PromptBuiltIns.ResetAllForTesting();
        // Setting a valid continuation fn should not throw
        RunStatements("prompt.setContinuation((ctx) => { return \"... \"; });");
        Assert.NotNull(PromptBuiltIns.GetRegisteredContinuationFn());
    }

    [Fact]
    public void ResetContinuation_ClearsContinuationFn()
    {
        PromptBuiltIns.ResetAllForTesting();
        RunStatements("""
            prompt.setContinuation((ctx) => { return "... "; });
            prompt.resetContinuation();
            """);
        Assert.Null(PromptBuiltIns.GetRegisteredContinuationFn());
    }
}
