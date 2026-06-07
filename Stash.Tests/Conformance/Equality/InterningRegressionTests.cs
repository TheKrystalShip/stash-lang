using Stash.Bytecode;
using Stash.Runtime;

namespace Stash.Tests.Conformance.Equality;

/// <summary>
/// Regression tests for the constant-pool interning comparer.
///
/// <para>
/// After the P5 fold (<c>ChunkBuilder.StashValueComparer</c> deleted), the constant pool
/// uses <see cref="StashValue"/>'s own <see cref="IEquatable{T}"/> implementation, which
/// is <b>bit-level</b> on the <c>Float</c> tag.  The interning rule is now
/// <em>at-least-as-fine</em> as before:
/// </para>
///
/// <list type="bullet">
///   <item>Two values that were previously distinct entries remain distinct entries.</item>
///   <item>
///     Values that were previously merged because the old comparer used value-level
///     float equality (<c>AsFloat == AsFloat</c>) are now kept distinct: specifically,
///     <c>+0.0</c> and <c>-0.0</c> differ in bit representation and therefore produce
///     two entries instead of one.
///   </item>
///   <item>
///     Two NaN values with identical bit patterns remain merged into one entry (bit-level
///     equality treats them as the same key), which is correct for structural identity
///     in the constant pool.
///   </item>
/// </list>
///
/// <para>
/// <b>§Equality Decision Log DE-interning:</b>
/// The constant pool is a structural-identity cache, not a runtime equality mode.
/// It must not be confused with <see cref="StashEquality.OperatorEquals"/> (value semantics),
/// <see cref="StashEquality.SameValueZero"/> (collection membership), or
/// <see cref="StashEquality.StrictEquals"/> (assert semantics).
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class InterningRegressionTests
{
    // ── Direct ChunkBuilder.AddConstant tests (most precise, no source-level parsing) ──

    /// <summary>
    /// §Equality — interning-fold: +0.0 and -0.0 are bit-level distinct.
    ///
    /// <para>
    /// Under the old value-level <c>StashValueComparer</c>, both mapped to the same double
    /// value (0.0 == -0.0 in IEEE 754) and were merged to one pool entry.  After the P5
    /// fold, the bit-level comparer sees different bit patterns and keeps them as two entries.
    /// This is the principal behavioral change introduced by the interning fold.
    /// </para>
    /// </summary>
    [Fact]
    public void ConstantPool_PosZeroAndNegZero_AreDistinctEntries()
    {
        var builder = new ChunkBuilder();

        ushort idx0 = builder.AddConstant(StashValue.FromFloat(+0.0));
        ushort idx1 = builder.AddConstant(StashValue.FromFloat(-0.0));
        Chunk chunk = builder.Build();

        // Post-fold: bit patterns differ → two entries.
        Assert.NotEqual(idx0, idx1);
        Assert.Equal(2, chunk.Constants.Count(
            c => c.Tag == StashValueTag.Float));
    }

    /// <summary>
    /// §Equality — interning-fold: two NaN values with identical bit patterns merge to one entry.
    ///
    /// <para>
    /// The bit-level comparer treats same-bit NaN as the same key, so the second
    /// <c>AddConstant</c> call returns the index of the first entry.
    /// </para>
    /// </summary>
    [Fact]
    public void ConstantPool_SameBitNaN_MergesToOneEntry()
    {
        var builder = new ChunkBuilder();

        // Both are the canonical IEEE 754 quiet NaN bit pattern.
        double nan1 = double.NaN;
        double nan2 = double.NaN;

        ushort idx0 = builder.AddConstant(StashValue.FromFloat(nan1));
        ushort idx1 = builder.AddConstant(StashValue.FromFloat(nan2));
        Chunk chunk = builder.Build();

        // Same bit pattern → same entry (structural identity).
        Assert.Equal(idx0, idx1);
        Assert.Equal(1, chunk.Constants.Count(
            c => c.Tag == StashValueTag.Float));
    }

    /// <summary>
    /// §Equality — interning-fold: int-tagged 1 and float-tagged 1.0 remain distinct entries.
    ///
    /// <para>
    /// Different <see cref="StashValueTag"/> → different keys regardless of whether the
    /// comparer is bit-level or value-level.  This was true before the fold and remains
    /// true after it.
    /// </para>
    /// </summary>
    [Fact]
    public void ConstantPool_IntOneAndFloatOne_AreDistinctEntries()
    {
        var builder = new ChunkBuilder();

        ushort idx0 = builder.AddConstant(StashValue.FromInt(1));
        ushort idx1 = builder.AddConstant(StashValue.FromFloat(1.0));
        Chunk chunk = builder.Build();

        Assert.NotEqual(idx0, idx1);
        Assert.Equal(1, chunk.Constants.Count(c => c.Tag == StashValueTag.Int));
        Assert.Equal(1, chunk.Constants.Count(c => c.Tag == StashValueTag.Float));
    }

    /// <summary>
    /// §Equality — interning-fold: int-tagged and byte-tagged values with the same numeric
    /// payload remain distinct entries because their tags differ.
    /// </summary>
    [Fact]
    public void ConstantPool_IntAndByte_SameNumericValue_AreDistinctEntries()
    {
        var builder = new ChunkBuilder();

        ushort idx0 = builder.AddConstant(StashValue.FromInt(1));
        ushort idx1 = builder.AddConstant(StashValue.FromByte(1));
        Chunk chunk = builder.Build();

        Assert.NotEqual(idx0, idx1);
        Assert.Equal(1, chunk.Constants.Count(c => c.Tag == StashValueTag.Int));
        Assert.Equal(1, chunk.Constants.Count(c => c.Tag == StashValueTag.Byte));
    }

    // ── Source-compilation tests (verify through the compile pipeline) ────────

    /// <summary>
    /// §Equality — interning-fold: a script using <c>let x = 1; let y = 1.0;</c> produces
    /// two distinct constant-pool entries because the two literals have different tags.
    ///
    /// <para>
    /// Both values go through <c>LoadK</c> (not an immediate-load specialisation), so the
    /// constant pool is exercised.  The fold does not change this case — different tags were
    /// already distinct under the old comparer.
    /// </para>
    /// </summary>
    [Fact]
    public void CompileSource_IntLiteralAndFloatLiteral_AreDistinctPoolEntries()
    {
        Chunk chunk = CompileSource("let x = 1; let y = 1.0;");

        // Exactly one int-tagged constant and one float-tagged constant.
        int intCount   = chunk.Constants.Count(c => c.Tag == StashValueTag.Int);
        int floatCount = chunk.Constants.Count(c => c.Tag == StashValueTag.Float);

        Assert.Equal(1, intCount);
        Assert.Equal(1, floatCount);
    }

    /// <summary>
    /// §Equality — interning-fold: the same distinctness holds with optimizations disabled
    /// (<c>enableDce: false</c>).  Int and float constants are never collapsed regardless
    /// of the optimisation pipeline state.
    /// </summary>
    [Fact]
    public void CompileSource_NoOptimize_IntAndFloatLiterals_RemainsDistinct()
    {
        Chunk chunk = CompileSource("let x = 1; let y = 1.0;", enableDce: false);

        int intCount   = chunk.Constants.Count(c => c.Tag == StashValueTag.Int);
        int floatCount = chunk.Constants.Count(c => c.Tag == StashValueTag.Float);

        Assert.Equal(1, intCount);
        Assert.Equal(1, floatCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Chunk CompileSource(string source, bool enableDce = true)
    {
        var lexer   = new Stash.Lexing.Lexer(source, "<test>");
        var tokens  = lexer.ScanTokens();
        var parser  = new Stash.Parsing.Parser(tokens);
        var stmts   = parser.ParseProgram();
        Stash.Resolution.SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts, enableDce);
    }
}
