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

    // ── SA01xx — Control Flow ────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0101 = new("SA0101", "Break outside loop", DiagnosticLevel.Error, "Control flow", "'break' used outside of a loop.");
    public static readonly DiagnosticDescriptor SA0102 = new("SA0102", "Continue outside loop", DiagnosticLevel.Error, "Control flow", "'continue' used outside of a loop.");
    public static readonly DiagnosticDescriptor SA0103 = new("SA0103", "Return outside function", DiagnosticLevel.Error, "Control flow", "'return' used outside of a function.");
    public static readonly DiagnosticDescriptor SA0104 = new("SA0104", "Unreachable code", DiagnosticLevel.Information, "Control flow", "Unreachable code detected.");

    // ── SA02xx — Declarations ────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0201 = new("SA0201", "Unused declaration", DiagnosticLevel.Information, "Declarations", "{0} '{1}' is declared but never used.");
    public static readonly DiagnosticDescriptor SA0202 = new("SA0202", "Undefined identifier", DiagnosticLevel.Warning, "Declarations", "'{0}' is not defined.");
    public static readonly DiagnosticDescriptor SA0203 = new("SA0203", "Constant reassignment", DiagnosticLevel.Error, "Declarations", "Cannot reassign constant '{0}'.");

    // ── SA03xx — Type Safety ─────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA0301 = new("SA0301", "Variable type mismatch", DiagnosticLevel.Warning, "Type safety", "Variable '{0}' is declared as '{1}' but initialized with '{2}'.");
    public static readonly DiagnosticDescriptor SA0302 = new("SA0302", "Constant type mismatch", DiagnosticLevel.Warning, "Type safety", "Constant '{0}' is declared as '{1}' but initialized with '{2}'.");
    public static readonly DiagnosticDescriptor SA0303 = new("SA0303", "Unknown type", DiagnosticLevel.Warning, "Type safety", "Unknown type '{0}'.");
    public static readonly DiagnosticDescriptor SA0304 = new("SA0304", "Field type mismatch", DiagnosticLevel.Warning, "Type safety", "Cannot assign value of type '{0}' to field '{1}' of type '{2}'.");
    public static readonly DiagnosticDescriptor SA0305 = new("SA0305", "Variable assignment type mismatch", DiagnosticLevel.Warning, "Type safety", "Cannot assign value of type '{0}' to variable '{1}' of type '{2}'.");

    // ── SA04xx — Functions & Calls ───────────────────────────────────
    public static readonly DiagnosticDescriptor SA0401 = new("SA0401", "User function arity mismatch", DiagnosticLevel.Error, "Functions & calls", "Expected {0} arguments but got {1}.");
    public static readonly DiagnosticDescriptor SA0402 = new("SA0402", "Built-in function arity mismatch", DiagnosticLevel.Error, "Functions & calls", "Expected {0} arguments but got {1}.");
    public static readonly DiagnosticDescriptor SA0403 = new("SA0403", "Argument type mismatch", DiagnosticLevel.Warning, "Functions & calls", "Argument '{0}' expects type '{1}' but got '{2}'.");

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
        dict[SA0101.Code] = SA0101;
        dict[SA0102.Code] = SA0102;
        dict[SA0103.Code] = SA0103;
        dict[SA0104.Code] = SA0104;
        dict[SA0201.Code] = SA0201;
        dict[SA0202.Code] = SA0202;
        dict[SA0203.Code] = SA0203;
        dict[SA0301.Code] = SA0301;
        dict[SA0302.Code] = SA0302;
        dict[SA0303.Code] = SA0303;
        dict[SA0304.Code] = SA0304;
        dict[SA0305.Code] = SA0305;
        dict[SA0401.Code] = SA0401;
        dict[SA0402.Code] = SA0402;
        dict[SA0403.Code] = SA0403;
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
        return dict.ToFrozenDictionary();
    }
}
