namespace Stash.Analysis;

using System.Collections.Generic;
using System.Collections.Frozen;

/// <summary>
/// Central registry of all diagnostic codes produced by the Stash analysis engine.
/// Every semantic diagnostic must reference a descriptor from this class.
/// </summary>
public static class DiagnosticDescriptors
{
    // ── SA00xx — Infrastructure ──────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0001 = new("SA0001", "Unknown diagnostic code in suppression directive", DiagnosticLevel.Warning, "Infrastructure", "Unknown diagnostic code '{0}' in suppression directive.");
    public static readonly DiagnosticDescriptor SA0002 = new("SA0002", "Malformed diagnostic code in suppression directive", DiagnosticLevel.Warning, "Infrastructure", "Malformed diagnostic code '{0}' in suppression directive. Expected format: SA followed by 4 digits.");
    public static readonly DiagnosticDescriptor SA0003 = new("SA0003", "Unused suppression directive", DiagnosticLevel.Warning, "Infrastructure", "Suppression directive for '{0}' did not suppress any diagnostics.");

    // ── SA01xx — Control Flow ────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0101 = new("SA0101", "Break outside loop", DiagnosticLevel.Error, "Control flow", "'break' used outside of a loop.");
    public static readonly DiagnosticDescriptor SA0102 = new("SA0102", "Continue outside loop", DiagnosticLevel.Error, "Control flow", "'continue' used outside of a loop.");
    public static readonly DiagnosticDescriptor SA0103 = new("SA0103", "Return outside function", DiagnosticLevel.Error, "Control flow", "'return' used outside of a function.");
    public static readonly DiagnosticDescriptor SA0104 = new("SA0104", "Unreachable code", DiagnosticLevel.Information, "Control flow", "Unreachable code detected.");
    public static readonly DiagnosticDescriptor SA0105 = new("SA0105", "Empty block body", DiagnosticLevel.Information, "Control flow", "Empty {0} body.");
    public static readonly DiagnosticDescriptor SA0106 = new("SA0106", "Unreachable code after terminating branches", DiagnosticLevel.Information, "Control flow", "Unreachable code: all preceding branches unconditionally return or throw.");
    public static readonly DiagnosticDescriptor SA0109 = new("SA0109", "Cyclomatic complexity too high", DiagnosticLevel.Information, "Control flow", "Cyclomatic complexity of function '{0}' is {1}, exceeds threshold of {2}.");

    // ── SA02xx — Declarations ────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0201 = new("SA0201", "Unused declaration", DiagnosticLevel.Information, "Declarations", "{0} '{1}' is declared but never used.");
    public static readonly DiagnosticDescriptor SA0202 = new("SA0202", "Undefined identifier", DiagnosticLevel.Warning, "Declarations", "'{0}' is not defined.");
    public static readonly DiagnosticDescriptor SA0203 = new("SA0203", "Constant reassignment", DiagnosticLevel.Error, "Declarations", "Cannot reassign constant '{0}'.", FixApplicability.Unsafe);
    public static readonly DiagnosticDescriptor SA0205 = new("SA0205", "Variable could be constant", DiagnosticLevel.Information, "Declarations", "Variable '{0}' is never reassigned. Consider using 'const' instead of 'let'.", FixApplicability.Safe);
    public static readonly DiagnosticDescriptor SA0206 = new("SA0206", "Unused parameter", DiagnosticLevel.Information, "Declarations", "Parameter '{0}' is declared but never used.");
    public static readonly DiagnosticDescriptor SA0207 = new("SA0207", "Shadow variable", DiagnosticLevel.Warning, "Declarations", "Variable '{0}' shadows an outer variable with the same name.");
    public static readonly DiagnosticDescriptor SA0208 = new("SA0208", "Dead store", DiagnosticLevel.Information, "Declarations", "Dead store: value assigned to '{0}' is overwritten before being read.");
    public static readonly DiagnosticDescriptor SA0209 = new("SA0209", "Naming convention violation", DiagnosticLevel.Information, "Declarations", "Name '{0}' does not follow {1} convention.");
    public static readonly DiagnosticDescriptor SA0210 = new("SA0210", "Variable used before assignment on all paths", DiagnosticLevel.Warning, "Declarations", "Variable '{0}' may be used before it is assigned on all code paths.");

    // ── SA03xx — Type Safety ─────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0301 = new("SA0301", "Variable type mismatch", DiagnosticLevel.Warning, "Type safety", "Variable '{0}' is declared as '{1}' but initialized with '{2}'.");
    public static readonly DiagnosticDescriptor SA0302 = new("SA0302", "Constant type mismatch", DiagnosticLevel.Warning, "Type safety", "Constant '{0}' is declared as '{1}' but initialized with '{2}'.");
    public static readonly DiagnosticDescriptor SA0303 = new("SA0303", "Unknown type", DiagnosticLevel.Warning, "Type safety", "Unknown type '{0}'.");
    public static readonly DiagnosticDescriptor SA0304 = new("SA0304", "Field type mismatch", DiagnosticLevel.Warning, "Type safety", "Cannot assign value of type '{0}' to field '{1}' of type '{2}'.");
    public static readonly DiagnosticDescriptor SA0305 = new("SA0305", "Variable assignment type mismatch", DiagnosticLevel.Warning, "Type safety", "Cannot assign value of type '{0}' to variable '{1}' of type '{2}'.");
    public static readonly DiagnosticDescriptor SA0308 = new("SA0308", "Possible null access", DiagnosticLevel.Warning, "Type safety", "Possible null access: '{0}' may be null.");
    public static readonly DiagnosticDescriptor SA0309 = new("SA0309", "Null access on unguarded path", DiagnosticLevel.Warning, "Type safety", "'{0}' may be null at this point. Assign a value or add a null check before accessing it.");
    public static readonly DiagnosticDescriptor SA0310 = new("SA0310", "Non-exhaustive switch on enum", DiagnosticLevel.Warning, "Type safety", "Switch on enum '{0}' does not cover all variants. Missing: {1}.");

    // ── SA04xx — Functions & Calls ───────────────────────────────────
    public static readonly DiagnosticDescriptor SA0401 = new("SA0401", "User function arity mismatch", DiagnosticLevel.Error, "Functions & calls", "Expected {0} arguments but got {1}.");
    public static readonly DiagnosticDescriptor SA0402 = new("SA0402", "Built-in function arity mismatch", DiagnosticLevel.Error, "Functions & calls", "Expected {0} arguments but got {1}.");
    public static readonly DiagnosticDescriptor SA0403 = new("SA0403", "Argument type mismatch", DiagnosticLevel.Warning, "Functions & calls", "Argument '{0}' expects type '{1}' but got '{2}'.");
    public static readonly DiagnosticDescriptor SA0404 = new("SA0404", "Missing return", DiagnosticLevel.Warning, "Functions & calls", "Not all code paths return a value in function '{0}'.");
    public static readonly DiagnosticDescriptor SA0405 = new("SA0405", "Too many parameters", DiagnosticLevel.Information, "Functions & calls", "Function '{0}' has {1} parameters, exceeds threshold of {2}.");

    // ── SA05xx — Spread / Rest ───────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0501 = new("SA0501", "Spread type mismatch (array context)", DiagnosticLevel.Warning, "Spread / Rest", "Spread argument has type '{0}', expected 'array'.");
    public static readonly DiagnosticDescriptor SA0502 = new("SA0502", "Spread type mismatch (dict context)", DiagnosticLevel.Warning, "Spread / Rest", "Spread argument has type '{0}', expected 'dict' or struct instance.");
    public static readonly DiagnosticDescriptor SA0503 = new("SA0503", "Spreading null literal", DiagnosticLevel.Warning, "Spread / Rest", "Spreading 'null' will always fail at runtime.");
    public static readonly DiagnosticDescriptor SA0504 = new("SA0504", "Unnecessary spread of array literal", DiagnosticLevel.Information, "Spread / Rest", "Unnecessary spread of array literal in function call. Pass the elements as direct arguments instead.");
    public static readonly DiagnosticDescriptor SA0505 = new("SA0505", "Empty spread", DiagnosticLevel.Information, "Spread / Rest", "Spreading an empty {0} literal has no effect.");
    public static readonly DiagnosticDescriptor SA0506 = new("SA0506", "Too many arguments with spread", DiagnosticLevel.Error, "Spread / Rest", "At least {0} arguments provided but '{1}' expects at most {2}.");

    // ── SA07xx — Commands ────────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0701 = new("SA0701", "Nested elevate", DiagnosticLevel.Warning, "Commands", "Nested 'elevate' has no effect. The outer elevation context already applies.");
    public static readonly DiagnosticDescriptor SA0702 = new("SA0702", "Retry shell commands only", DiagnosticLevel.Warning, "Commands", "Retry body contains only shell commands, which never throw. Add an 'until' clause to check command results, or use '$!(...)' strict commands.");
    public static readonly DiagnosticDescriptor SA0703 = new("SA0703", "Retry zero attempts", DiagnosticLevel.Warning, "Commands", "'retry' with 0 attempts will never execute the body.");
    public static readonly DiagnosticDescriptor SA0704 = new("SA0704", "Retry single attempt", DiagnosticLevel.Information, "Commands", "'retry' with 1 attempt will never retry. Consider removing the retry block.");
    public static readonly DiagnosticDescriptor SA0705 = new("SA0705", "Invalid on filter", DiagnosticLevel.Warning, "Commands", "'on' filter expects an array of error type names (identifiers or string literals).");
    public static readonly DiagnosticDescriptor SA0706 = new("SA0706", "Invalid on option", DiagnosticLevel.Warning, "Commands", "'on' option expects an array of error type names.");
    public static readonly DiagnosticDescriptor SA0707 = new("SA0707", "Invalid until clause", DiagnosticLevel.Warning, "Commands", "'until' clause expects a callable expression (lambda or function reference).");
    public static readonly DiagnosticDescriptor SA0708 = new("SA0708", "Backoff without delay", DiagnosticLevel.Information, "Commands", "'backoff' has no effect without a non-zero 'delay'.");
    public static readonly DiagnosticDescriptor SA0709 = new("SA0709", "Retry no throwable operations", DiagnosticLevel.Information, "Commands", "Retry body contains no operations that can throw. The retry block will always succeed on the first attempt.");

    // ── SA08xx — Imports ─────────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0801 = new("SA0801", "Dynamic import path", DiagnosticLevel.Information, "Imports", "Dynamic import path cannot be resolved statically. Autocomplete, go-to-definition, and other editor features will not be available for this import.");
    public static readonly DiagnosticDescriptor SA0802 = new("SA0802", "Unused import", DiagnosticLevel.Warning, "Imports", "Import '{0}' is never used.", FixApplicability.Safe);
    public static readonly DiagnosticDescriptor SA0804 = new("SA0804", "Import statements not in canonical order", DiagnosticLevel.Information, "Imports", "Import statements are not in canonical order.", FixApplicability.Safe);

    // ── SA09xx — Style ───────────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0901 = new("SA0901", "Unnecessary else after return", DiagnosticLevel.Information, "Style", "Unnecessary 'else' after '{0}' in then-branch. The else body can be unindented.");

    // ── SA10xx — Complexity ──────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA1002 = new("SA1002", "Nesting depth too high", DiagnosticLevel.Information, "Complexity", "Nesting depth of {0} exceeds threshold of {1} in function '{2}'.");

    // ── SA11xx — Best Practices ─────────────────────────────────────
    public static readonly DiagnosticDescriptor SA1102 = new("SA1102", "Self-assignment", DiagnosticLevel.Warning, "Best Practices", "Self-assignment: '{0}' is assigned to itself.", FixApplicability.Safe);
    public static readonly DiagnosticDescriptor SA1103 = new("SA1103", "Duplicate case value", DiagnosticLevel.Warning, "Best Practices", "Duplicate case value: '{0}'.");
    public static readonly DiagnosticDescriptor SA1105 = new("SA1105", "Unnecessary block statement", DiagnosticLevel.Information, "Best Practices", "Unnecessary block statement. This block does not create a new scope and can be removed.");
    public static readonly DiagnosticDescriptor SA1106 = new("SA1106", "Self-comparison", DiagnosticLevel.Warning, "Best Practices", "Self-comparison: '{0}' is compared to itself. This is always {1}.");
    public static readonly DiagnosticDescriptor SA1107 = new("SA1107", "Constant condition", DiagnosticLevel.Warning, "Best Practices", "Constant condition: this {0} condition is always {1}.");
    public static readonly DiagnosticDescriptor SA1108 = new("SA1108", "Unreachable loop", DiagnosticLevel.Warning, "Best Practices", "Loop body always exits on first iteration. This loop will execute at most once.");

    // ── SA12xx — Performance ─────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA1201 = new("SA1201", "Accumulating spread in loop", DiagnosticLevel.Warning, "Performance", "Spreading '{0}' into itself inside a loop creates a copy each iteration (O(n²)). Use arr.push() or dict.set() instead.");

    // ── SA13xx — Security ────────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA1301 = new("SA1301", "Hardcoded credentials", DiagnosticLevel.Warning, "Security", "Variable '{0}' appears to contain hardcoded credentials. Use environment variables or a secrets manager instead.");
    public static readonly DiagnosticDescriptor SA1302 = new("SA1302", "Unsafe command interpolation", DiagnosticLevel.Warning, "Security", "String interpolation in shell command may allow command injection. Validate or escape '{0}' before use.");

    // ── SA14xx — Suggestions ─────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA1401 = new("SA1401", "Use optional chaining", DiagnosticLevel.Information, "Suggestions", "Use optional chaining: '{0}?.{1}' instead of null check with member access.", FixApplicability.Unsafe);
    public static readonly DiagnosticDescriptor SA1402 = new("SA1402", "Use null coalescing", DiagnosticLevel.Information, "Suggestions", "Use null coalescing: '{0} ?? {1}' instead of null check with ternary.", FixApplicability.Unsafe);
    /// <summary>
    /// Lookup table from code string to descriptor for suppression validation.
    /// </summary>
    public static readonly FrozenDictionary<string, DiagnosticDescriptor> AllByCode = BuildCodeLookup();

    private static FrozenDictionary<string, DiagnosticDescriptor> BuildCodeLookup()
    {
        var dict = new Dictionary<string, DiagnosticDescriptor>();
        // Use reflection-free manual registration
        dict[SA0001.Code] = SA0001;
        dict[SA0002.Code] = SA0002;
        dict[SA0003.Code] = SA0003;
        dict[SA0101.Code] = SA0101;
        dict[SA0102.Code] = SA0102;
        dict[SA0103.Code] = SA0103;
        dict[SA0104.Code] = SA0104;
        dict[SA0105.Code] = SA0105;
        dict[SA0106.Code] = SA0106;
        dict[SA0109.Code] = SA0109;
        dict[SA0201.Code] = SA0201;
        dict[SA0202.Code] = SA0202;
        dict[SA0203.Code] = SA0203;
        dict[SA0205.Code] = SA0205;
        dict[SA0206.Code] = SA0206;
        dict[SA0207.Code] = SA0207;
        dict[SA0208.Code] = SA0208;
        dict[SA0209.Code] = SA0209;
        dict[SA0210.Code] = SA0210;
        dict[SA0301.Code] = SA0301;
        dict[SA0302.Code] = SA0302;
        dict[SA0303.Code] = SA0303;
        dict[SA0304.Code] = SA0304;
        dict[SA0305.Code] = SA0305;
        dict[SA0308.Code] = SA0308;
        dict[SA0309.Code] = SA0309;
        dict[SA0310.Code] = SA0310;
        dict[SA0401.Code] = SA0401;
        dict[SA0402.Code] = SA0402;
        dict[SA0403.Code] = SA0403;
        dict[SA0404.Code] = SA0404;
        dict[SA0405.Code] = SA0405;
        dict[SA0501.Code] = SA0501;
        dict[SA0502.Code] = SA0502;
        dict[SA0503.Code] = SA0503;
        dict[SA0504.Code] = SA0504;
        dict[SA0505.Code] = SA0505;
        dict[SA0506.Code] = SA0506;
        dict[SA0701.Code] = SA0701;
        dict[SA0702.Code] = SA0702;
        dict[SA0703.Code] = SA0703;
        dict[SA0704.Code] = SA0704;
        dict[SA0705.Code] = SA0705;
        dict[SA0706.Code] = SA0706;
        dict[SA0707.Code] = SA0707;
        dict[SA0708.Code] = SA0708;
        dict[SA0709.Code] = SA0709;
        dict[SA0801.Code] = SA0801;
        dict[SA0802.Code] = SA0802;
        dict[SA0804.Code] = SA0804;
        dict[SA0901.Code] = SA0901;
        dict[SA1002.Code] = SA1002;
        dict[SA1102.Code] = SA1102;
        dict[SA1103.Code] = SA1103;
        dict[SA1105.Code] = SA1105;
        dict[SA1106.Code] = SA1106;
        dict[SA1107.Code] = SA1107;
        dict[SA1108.Code] = SA1108;
        dict[SA1201.Code] = SA1201;
        dict[SA1301.Code] = SA1301;
        dict[SA1302.Code] = SA1302;
        dict[SA1401.Code] = SA1401;
        dict[SA1402.Code] = SA1402;
        return dict.ToFrozenDictionary();
    }
}
