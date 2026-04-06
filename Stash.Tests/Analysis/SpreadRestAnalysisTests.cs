using Stash.Analysis;

namespace Stash.Tests.Analysis;

public class SpreadRestAnalysisTests : AnalysisTestBase
{
    // === Type Mismatch Tests (SA0501) ===

    // Test 51: Spreading int in array context
    [Fact]
    public void SpreadInArray_IntType_EmitsSA0501()
    {
        var diagnostics = Validate("let x: int = 5; let y = [...x];");
        Assert.Contains(diagnostics, d => d.Code == "SA0501");
    }

    // Test 52: Spreading string in array context
    [Fact]
    public void SpreadInArray_StringType_EmitsSA0501()
    {
        var diagnostics = Validate("let s: string = \"hi\"; let y = [...s];");
        Assert.Contains(diagnostics, d => d.Code == "SA0501");
    }

    // Test 53: Spreading dict in function call context (expects array)
    [Fact]
    public void SpreadInCall_DictType_EmitsSA0501()
    {
        var diagnostics = Validate("let d: dict = {}; fn f(a) { return a; } f(...d);");
        Assert.Contains(diagnostics, d => d.Code == "SA0501");
    }

    // Test 54: Spreading int in dict literal context
    [Fact]
    public void SpreadInDict_IntType_EmitsSA0502()
    {
        var diagnostics = Validate("let x: int = 5; let r = { ...x };");
        Assert.Contains(diagnostics, d => d.Code == "SA0502");
    }

    // Test 55: Spreading array in dict literal context
    [Fact]
    public void SpreadInDict_ArrayType_EmitsSA0502()
    {
        var diagnostics = Validate("let a: array = []; let r = { ...a };");
        Assert.Contains(diagnostics, d => d.Code == "SA0502");
    }

    // === Null Literal Tests (SA0503) ===

    // Test 56: Spreading null in array literal
    [Fact]
    public void SpreadNull_InArray_EmitsSA0503()
    {
        var diagnostics = Validate("let r = [...null];");
        Assert.Contains(diagnostics, d => d.Code == "SA0503");
    }

    // Test 57: Spreading null in dict literal
    [Fact]
    public void SpreadNull_InDict_EmitsSA0503()
    {
        var diagnostics = Validate("let r = { ...null };");
        Assert.Contains(diagnostics, d => d.Code == "SA0503");
    }

    // Test 58: Spreading null in function call
    [Fact]
    public void SpreadNull_InCall_EmitsSA0503()
    {
        var diagnostics = Validate("fn f(a) { return a; } f(...null);");
        Assert.Contains(diagnostics, d => d.Code == "SA0503");
    }

    // === Arity Tests (SA0506) ===

    // Test 59: Non-spread count exceeds max arity
    [Fact]
    public void SpreadInCall_NonSpreadExceedsArity_EmitsSA0506()
    {
        var diagnostics = Validate("fn f(a, b) { return a + b; } let x = [1]; f(1, 2, 3, ...x);");
        Assert.Contains(diagnostics, d => d.Code == "SA0506");
    }

    // Test 60: Spread with unknown count — no SA0506 or SA0401
    [Fact]
    public void SpreadInCall_SpreadOnly_NoArityDiagnostic()
    {
        var diagnostics = Validate("fn f(a, b) { return a + b; } let x = [1]; f(...x);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0506");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0401");
    }

    // Test 61: Spread present — no arity diagnostic even if total could exceed
    [Fact]
    public void SpreadInCall_WithSpreadPresent_NoArityDiagnostic()
    {
        var diagnostics = Validate("fn f(a, b) { return a + b; } fn getArgs() { return [2]; } f(1, ...getArgs());");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0506");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0401");
    }

    // Test 62: Rest parameter absorbs extra args — no arity diagnostic
    [Fact]
    public void SpreadInCall_RestParamAbsorbs_NoArityDiagnostic()
    {
        var diagnostics = Validate("fn f(a, ...rest) { return [a, rest]; } fn getMore() { return [3]; } f(1, 2, ...getMore());");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0506");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0401");
    }

    // === Unused Rest Variable in Destructuring (SA0201) ===

    // Test 64: Unused rest variable in array destructuring (registered as Variable, not Parameter)
    [Fact]
    public void RestInDestructuring_Unused_EmitsSA0201()
    {
        var diagnostics = Validate("fn test() { let [head, ...tail] = [1, 2, 3]; let x = head; }");
        Assert.Contains(diagnostics, d => d.Code == "SA0201" && d.Message.Contains("'tail'"));
    }

    // === Unnecessary Spread Tests (SA0504, SA0505) ===

    // Test 65: Spreading an array literal in function call — unnecessary spread
    [Fact]
    public void SpreadLiteral_InCall_EmitsSA0504()
    {
        var diagnostics = Validate("fn f(a, b) { return a + b; } f(...[1, 2]);");
        Assert.Contains(diagnostics, d => d.Code == "SA0504");
    }

    // Test 66: Spreading empty array literal — no effect
    [Fact]
    public void SpreadEmptyArray_InArrayLiteral_EmitsSA0505()
    {
        var diagnostics = Validate("let r = [1, ...[], 2];");
        Assert.Contains(diagnostics, d => d.Code == "SA0505");
    }

    // Test 67: Spreading empty dict literal — no effect
    [Fact]
    public void SpreadEmptyDict_InDictLiteral_EmitsSA0505()
    {
        var diagnostics = Validate("let r = { ...{}, a: 1 };");
        Assert.Contains(diagnostics, d => d.Code == "SA0505");
    }

    // === Type Hint Tests ===

    // Test 68: Unknown type in rest parameter type hint emits SA0303
    [Fact]
    public void RestParam_UnknownTypeHint_EmitsSA0303()
    {
        var diagnostics = Validate("fn f(...args: Frobnitz) { return args; }");
        Assert.Contains(diagnostics, d => d.Code == "SA0303");
    }

    // === Negative Tests (no diagnostic) ===

    // Test 69: Spreading typed array in array literal — valid, no SA0501
    [Fact]
    public void SpreadTypedArray_InArrayLiteral_NoSA0501()
    {
        var diagnostics = Validate("let a: array = [1]; let r = [...a, 3];");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0501");
    }

    // Test 70: Spreading typed dict in dict literal — valid, no SA0502
    [Fact]
    public void SpreadTypedDict_InDictLiteral_NoSA0502()
    {
        var diagnostics = Validate("let d: dict = { a: 1 }; let r = { ...d, b: 2 };");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0502");
    }

    // Test 71: Spreading struct instance into dict literal — valid, no SA0502
    [Fact]
    public void SpreadStructInstance_InDictLiteral_NoSA0502()
    {
        var diagnostics = Validate("struct S { x } let s = S { x: 1 }; let r = { ...s };");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0502");
    }

    // Test 72: Type mismatch on first arg fires SA0403 but spread arg is not checked
    [Fact]
    public void SpreadInCall_TypeMismatchOnFixedArg_EmitsSA0403_NotOnSpread()
    {
        var diagnostics = Validate(
            "fn f(name: string, count: int) { return [name, count]; } let x = [1]; f(123, ...x);");
        Assert.Contains(diagnostics, d => d.Code == "SA0403");
        // The spread argument itself should not produce SA0403
        var sa0403Diags = diagnostics.Where(d => d.Code == "SA0403").ToList();
        Assert.Single(sa0403Diags);
    }

    // Regression: typed rest param should not cause SA0501 when spread in body
    [Fact]
    public void TypedRestParam_SpreadInBody_NoSA0501()
    {
        var diagnostics = Validate("fn process(...items: string) { let copy = [...items]; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0501");
    }
}
