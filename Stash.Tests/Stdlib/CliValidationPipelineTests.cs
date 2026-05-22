namespace Stash.Tests.Stdlib;

using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for P5 of the cli-arg-parsing feature: the validation pipeline.
///
/// Validated order per spec:
///   1. Token-level (tested in P3)
///   2. Type conversion (tested in P3)
///   3. Choices (tested in P3)
///   4. min / max / pattern → CliValidationFailed
///   5. User validate callback → CliValidationFailed
///
/// Additional P5 coverage:
///   - Construction-time: min/max on non-numeric type → CliSchemaError
///   - Construction-time: pattern on non-string type → CliSchemaError
///   - Missing-required runs AFTER per-argument validation (ordering)
/// </summary>
public class CliValidationPipelineTests : StashTestBase
{
    // =========================================================================
    // min / max — int
    // =========================================================================

    [Fact]
    public void Min_IntWithinBound_Passes()
    {
        var result = Run("""
            let schema = cli.schema({ count: cli.option("int", { min: 1 }) });
            let r = cli.tryParse(schema, ["--count", "5"]);
            let result = r.value.count;
        """);
        Assert.Equal(5L, result);
    }

    [Fact]
    public void Min_IntAtBoundary_Passes()
    {
        var result = Run("""
            let schema = cli.schema({ count: cli.option("int", { min: 5 }) });
            let r = cli.tryParse(schema, ["--count", "5"]);
            let result = r.value.count;
        """);
        Assert.Equal(5L, result);
    }

    [Fact]
    public void Min_IntBelowBound_ParseResultNotOk()
    {
        // tryParse captures the error rather than throwing — result.ok is false.
        var r = Run("""
            let schema = cli.schema({ count: cli.option("int", { min: 5 }) });
            let r = cli.tryParse(schema, ["--count", "3"]);
            let result = r.ok;
        """);
        Assert.Equal(false, r);
    }

    [Fact]
    public void Min_IntBelowBound_ErrorIsCliValidationFailed()
    {
        var result = Run("""
            let schema = cli.schema({ count: cli.option("int", { min: 5 }) });
            let r = cli.tryParse(schema, ["--count", "3"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliValidationFailed", result);
    }

    [Fact]
    public void Max_IntAtBoundary_Passes()
    {
        var result = Run("""
            let schema = cli.schema({ retries: cli.option("int", { max: 10 }) });
            let r = cli.tryParse(schema, ["--retries", "10"]);
            let result = r.value.retries;
        """);
        Assert.Equal(10L, result);
    }

    [Fact]
    public void Max_IntAboveBound_ErrorIsCliValidationFailed()
    {
        var result = Run("""
            let schema = cli.schema({ retries: cli.option("int", { max: 10 }) });
            let r = cli.tryParse(schema, ["--retries", "11"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliValidationFailed", result);
    }

    [Fact]
    public void MinMax_IntWithinRange_Passes()
    {
        var result = Run("""
            let schema = cli.schema({ port: cli.option("int", { min: 1024, max: 65535 }) });
            let r = cli.tryParse(schema, ["--port", "8080"]);
            let result = r.value.port;
        """);
        Assert.Equal(8080L, result);
    }

    [Fact]
    public void MinMax_IntOutsideRange_ErrorIsCliValidationFailed()
    {
        var result = Run("""
            let schema = cli.schema({ port: cli.option("int", { min: 1024, max: 65535 }) });
            let r = cli.tryParse(schema, ["--port", "80"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliValidationFailed", result);
    }

    // =========================================================================
    // min / max — float
    // =========================================================================

    [Fact]
    public void Min_FloatWithinBound_Passes()
    {
        var result = Run("""
            let schema = cli.schema({ rate: cli.option("float", { min: 0.0 }) });
            let r = cli.tryParse(schema, ["--rate", "0.5"]);
            let result = r.value.rate;
        """);
        Assert.Equal(0.5, result);
    }

    [Fact]
    public void Max_FloatAboveBound_ErrorIsCliValidationFailed()
    {
        var result = Run("""
            let schema = cli.schema({ rate: cli.option("float", { max: 1.0 }) });
            let r = cli.tryParse(schema, ["--rate", "1.5"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliValidationFailed", result);
    }

    // =========================================================================
    // pattern — string
    // =========================================================================

    [Fact]
    public void Pattern_StringMatchesRegex_Passes()
    {
        var result = Run("""
            let schema = cli.schema({ name: cli.option("string", { pattern: "^[a-z]+$" }) });
            let r = cli.tryParse(schema, ["--name", "alice"]);
            let result = r.value.name;
        """);
        Assert.Equal("alice", result);
    }

    [Fact]
    public void Pattern_StringDoesNotMatchRegex_ErrorIsCliValidationFailed()
    {
        var result = Run("""
            let schema = cli.schema({ name: cli.option("string", { pattern: "^[a-z]+$" }) });
            let r = cli.tryParse(schema, ["--name", "Alice123"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliValidationFailed", result);
    }

    [Fact]
    public void Pattern_StringDoesNotMatch_ErrorMessageContainsPattern()
    {
        var result = Run("""
            let schema = cli.schema({ name: cli.option("string", { pattern: "^[a-z]+$" }) });
            let r = cli.tryParse(schema, ["--name", "Alice123"]);
            let result = r.error.message;
        """);
        var msg = Assert.IsType<string>(result);
        Assert.Contains("^[a-z]+$", msg);
    }

    // =========================================================================
    // validate callback — tri-return convention
    // =========================================================================

    [Fact]
    public void Validate_CallbackReturnsTrue_Passes()
    {
        var result = Run("""
            let schema = cli.schema({ n: cli.option("int", { validate: (v) => v % 2 == 0 }) });
            let r = cli.tryParse(schema, ["--n", "4"]);
            let result = r.value.n;
        """);
        Assert.Equal(4L, result);
    }

    [Fact]
    public void Validate_CallbackReturnsFalse_ErrorIsCliValidationFailed()
    {
        var result = Run("""
            let schema = cli.schema({ n: cli.option("int", { validate: (v) => v % 2 == 0 }) });
            let r = cli.tryParse(schema, ["--n", "3"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliValidationFailed", result);
    }

    [Fact]
    public void Validate_CallbackReturnsString_ErrorMessageIsString()
    {
        var result = Run("""
            let schema = cli.schema({ n: cli.option("int", {
                validate: (v) => {
                    if (v % 2 != 0) { return "must be even"; }
                    return true;
                }
            }) });
            let r = cli.tryParse(schema, ["--n", "3"]);
            let result = r.error.message;
        """);
        Assert.Equal("must be even", result);
    }

    [Fact]
    public void Validate_CallbackReturnsString_ErrorTypeIsCliValidationFailed()
    {
        var result = Run("""
            let schema = cli.schema({ n: cli.option("int", {
                validate: (v) => {
                    if (v < 0) { return "must be non-negative"; }
                    return true;
                }
            }) });
            let r = cli.tryParse(schema, ["--n", "-1"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliValidationFailed", result);
    }

    // =========================================================================
    // Ordering: choices runs BEFORE min/max/pattern (step 2 < step 3)
    // =========================================================================

    [Fact]
    public void Order_ChoicesBeforeMin_ChoicesViolationRaisesCliInvalidValue()
    {
        // choices runs at step 2 (CliInvalidValue); if the value is not in choices,
        // we should get CliInvalidValue, not CliValidationFailed from min.
        var result = Run("""
            let schema = cli.schema({ n: cli.option("int", { choices: [1, 2, 3], min: 0 }) });
            let r = cli.tryParse(schema, ["--n", "99"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliInvalidValue", result);
    }

    // =========================================================================
    // Ordering: missing-required runs AFTER per-argument validation
    // =========================================================================

    [Fact]
    public void Order_ValidationBeforeMissingRequired_ValidationErrorFirst()
    {
        // Provide an option with a bad value AND omit a required option.
        // Per spec, per-argument validation runs first, so we expect CliValidationFailed
        // for the bad value, not CliMissingRequired for the missing option.
        var result = Run("""
            let schema = cli.schema({
                n: cli.option("int", { min: 10 }),
                req: cli.option("string", { required: true })
            });
            let r = cli.tryParse(schema, ["--n", "1"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliValidationFailed", result);
    }

    // =========================================================================
    // Construction-time: min/max on non-numeric type → CliSchemaError
    // =========================================================================

    [Fact]
    public void SchemaTime_MinOnStringType_RaisesCliSchemaError()
    {
        var error = RunCapturingError("""
            let schema = cli.schema({ name: cli.option("string", { min: 1 }) });
            let result = "unreachable";
        """);
        Assert.IsAssignableFrom<CliSchemaError>(error);
    }

    [Fact]
    public void SchemaTime_MaxOnStringType_RaisesCliSchemaError()
    {
        var error = RunCapturingError("""
            let schema = cli.schema({ name: cli.option("string", { max: 10 }) });
            let result = "unreachable";
        """);
        Assert.IsAssignableFrom<CliSchemaError>(error);
    }

    [Fact]
    public void SchemaTime_MinOnBoolType_RaisesCliSchemaError()
    {
        var error = RunCapturingError("""
            let schema = cli.schema({ flag: cli.option("bool", { min: 0 }) });
            let result = "unreachable";
        """);
        Assert.IsAssignableFrom<CliSchemaError>(error);
    }

    [Fact]
    public void SchemaTime_MaxOnBoolType_RaisesCliSchemaError()
    {
        var error = RunCapturingError("""
            let schema = cli.schema({ flag: cli.option("bool", { max: 1 }) });
            let result = "unreachable";
        """);
        Assert.IsAssignableFrom<CliSchemaError>(error);
    }

    // =========================================================================
    // Construction-time: pattern on non-string type → CliSchemaError
    // =========================================================================

    [Fact]
    public void SchemaTime_PatternOnIntType_RaisesCliSchemaError()
    {
        var error = RunCapturingError("""
            let schema = cli.schema({ n: cli.option("int", { pattern: "^\\d+$" }) });
            let result = "unreachable";
        """);
        Assert.IsAssignableFrom<CliSchemaError>(error);
    }

    [Fact]
    public void SchemaTime_PatternOnFloatType_RaisesCliSchemaError()
    {
        var error = RunCapturingError("""
            let schema = cli.schema({ n: cli.option("float", { pattern: "^\\d+$" }) });
            let result = "unreachable";
        """);
        Assert.IsAssignableFrom<CliSchemaError>(error);
    }

    // =========================================================================
    // min/max on valid numeric types (sanity — should NOT raise CliSchemaError)
    // =========================================================================

    [Fact]
    public void SchemaTime_MinOnIntType_NoSchemaError()
    {
        // Should not throw at schema construction
        RunStatements("""
            let schema = cli.schema({ count: cli.option("int", { min: 0 }) });
        """);
    }

    [Fact]
    public void SchemaTime_MaxOnFloatType_NoSchemaError()
    {
        RunStatements("""
            let schema = cli.schema({ rate: cli.option("float", { max: 1.0 }) });
        """);
    }

    // =========================================================================
    // pattern on string type (sanity �� should NOT raise CliSchemaError)
    // =========================================================================

    [Fact]
    public void SchemaTime_PatternOnStringType_NoSchemaError()
    {
        RunStatements("""
            let schema = cli.schema({ name: cli.option("string", { pattern: "^[a-z]+$" }) });
        """);
    }

    // =========================================================================
    // validate callback — positional argument
    // =========================================================================

    [Fact]
    public void Validate_PositionalCallbackReturnsTrue_Passes()
    {
        var result = Run("""
            let schema = cli.schema({
                src: cli.positional("string", {
                    validate: (v) => str.endsWith(v, ".txt")
                })
            });
            let r = cli.tryParse(schema, ["input.txt"]);
            let result = r.value.src;
        """);
        Assert.Equal("input.txt", result);
    }

    [Fact]
    public void Validate_PositionalCallbackReturnsFalse_ErrorIsCliValidationFailed()
    {
        var result = Run("""
            let schema = cli.schema({
                src: cli.positional("string", {
                    validate: (v) => str.endsWith(v, ".txt")
                })
            });
            let r = cli.tryParse(schema, ["input.csv"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliValidationFailed", result);
    }

    [Fact]
    public void Validate_PositionalCallbackReturnsString_MessageUsed()
    {
        var result = Run("""
            let schema = cli.schema({
                src: cli.positional("string", {
                    validate: (v) => {
                        if (!str.endsWith(v, ".txt")) { return "must be a .txt file"; }
                        return true;
                    }
                })
            });
            let r = cli.tryParse(schema, ["input.csv"]);
            let result = r.error.message;
        """);
        Assert.Equal("must be a .txt file", result);
    }
}
