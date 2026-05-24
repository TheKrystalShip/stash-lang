using Stash.Bytecode;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Core;

/// <summary>
/// Invariant tests that cross-check <see cref="PrimitiveCapability.Extendable"/> flags
/// against the bytecode compiler's actual <c>extend</c> acceptance behaviour.
/// <para>
/// Mirrors the style of <see cref="IVMPrimitiveTypeInvariantTests"/>: data-driven over
/// <see cref="PrimitiveTypes.Entries"/> so any future entry automatically participates.
/// </para>
/// </summary>
public class PrimitiveCapabilityInvariantTests
{
    // =========================================================================
    // Infrastructure
    // =========================================================================

    /// <summary>
    /// Represents the outcome of attempting to execute an <c>extend</c> snippet.
    /// </summary>
    private sealed record ExecuteResult(
        bool HadParseError,
        RuntimeError? RuntimeError);

    /// <summary>
    /// Compile and execute a Stash snippet. Returns whether a parse error occurred
    /// (some primitive names are Stash keywords and are rejected at the parser level)
    /// and any <see cref="RuntimeError"/> raised during execution.
    /// </summary>
    private static ExecuteResult TryExecute(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        if (parser.Errors.Count > 0)
            return new ExecuteResult(HadParseError: true, RuntimeError: null);

        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        try
        {
            vm.Execute(chunk);
            return new ExecuteResult(HadParseError: false, RuntimeError: null);
        }
        catch (RuntimeError ex)
        {
            return new ExecuteResult(HadParseError: false, RuntimeError: ex);
        }
    }

    private static string ExtendSnippet(string typeName) =>
        $"extend {typeName} {{ fn __capability_test() {{ return 1; }} }}";

    // =========================================================================
    // Tests
    // =========================================================================

    /// <summary>
    /// Every primitive flagged <see cref="PrimitiveCapability.Extendable"/> must compile
    /// and execute an <c>extend</c> block without raising a <see cref="RuntimeError"/>.
    /// </summary>
    [Fact]
    public void ExtendablePrimitives_AcceptExtendBlock_WithoutError()
    {
        var extendableEntries = PrimitiveTypes.Entries
            .Where(e => e.Caps.HasFlag(PrimitiveCapability.Extendable))
            .ToList();

        Assert.NotEmpty(extendableEntries); // guard against an empty source

        foreach (var entry in extendableEntries)
        {
            var result = TryExecute(ExtendSnippet(entry.Name));
            Assert.True(
                !result.HadParseError && result.RuntimeError is null,
                $"extend {entry.Name} failed but PrimitiveCapability.Extendable is set. " +
                $"ParseError={result.HadParseError}, RuntimeError={result.RuntimeError?.Message}");
        }
    }

    /// <summary>
    /// Every primitive NOT flagged <see cref="PrimitiveCapability.Extendable"/> that can be
    /// parsed as a valid type name must raise a <see cref="RuntimeError"/> whose message
    /// contains <c>Cannot extend '&lt;name&gt;': not a known type.</c>.
    /// <para>
    /// Some primitive names (<c>null</c>, <c>struct</c>, <c>enum</c>) are Stash keywords
    /// that the parser does not accept as type names in <c>extend</c> blocks — they produce
    /// parse errors and never reach the runtime acceptance check. These entries are skipped
    /// here because testing them requires parser-level assertions, not runtime assertions.
    /// The parser-level rejection itself constitutes correct enforcement of the non-extendable
    /// contract for those names.
    /// </para>
    /// <para>
    /// Typed-array names (<c>int[]</c>, <c>float[]</c>, etc.) and runtime opaque primitive
    /// names (<c>Future</c>, <c>range</c>, <c>duration</c>, etc.) do parse successfully and
    /// are verified here to produce the expected runtime rejection.
    /// </para>
    /// </summary>
    [Fact]
    public void NonExtendablePrimitives_RejectExtendBlock_WithExpectedError()
    {
        var nonExtendableEntries = PrimitiveTypes.Entries
            .Where(e => !e.Caps.HasFlag(PrimitiveCapability.Extendable))
            .ToList();

        Assert.NotEmpty(nonExtendableEntries); // guard against an empty source

        int testedCount = 0;
        foreach (var entry in nonExtendableEntries)
        {
            var result = TryExecute(ExtendSnippet(entry.Name));

            if (result.HadParseError)
            {
                // This primitive name is a Stash keyword that the parser rejects before
                // reaching the runtime acceptance check (e.g. null, struct, enum). The
                // parse-level rejection is itself correct enforcement — skip runtime assertion.
                continue;
            }

            testedCount++;

            Assert.True(
                result.RuntimeError is not null,
                $"extend {entry.Name} succeeded but PrimitiveCapability.Extendable is NOT set; " +
                $"either flag the entry Extendable or ensure the compiler rejects it.");

            string expected = $"Cannot extend '{entry.Name}': not a known type.";
            Assert.Contains(
                expected,
                result.RuntimeError!.Message,
                StringComparison.Ordinal);
        }

        // Guard: at least some non-extendable entries must be testable at runtime
        // (i.e., not all of them can be parse-rejected).
        Assert.True(
            testedCount > 0,
            "All non-extendable primitives produced parse errors — none reached the runtime " +
            "acceptance check. This suggests the test's skip logic is too broad.");
    }
}
