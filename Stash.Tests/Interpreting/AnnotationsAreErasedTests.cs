namespace Stash.Tests.Interpreting;

/// <summary>
/// Verifies the type-hint erasure model: annotations on <c>let</c> / <c>const</c> /
/// <c>fn</c> bindings are advisory metadata for tooling and never produce runtime
/// checks, value coercion, or wrapping. See
/// <c>.kanban/2-in-progress/Type Hints — Canonical TypeExpression Refactor (Erasure Model).md</c>
/// (§2 and §5 Phase 2) for the decision record.
/// </summary>
public class AnnotationsAreErasedTests : StashTestBase
{
    // ── byte narrowing is removed: a literal that "fits" still stays a long ─────

    [Fact]
    public void ByteAnnotation_LiteralInRange_StaysInt()
    {
        // Pre-erasure this would have produced a byte. Now the literal is a long.
        Assert.Equal("int", Run("let b: byte = 200; let result = typeof(b);"));
    }

    [Fact]
    public void ByteAnnotation_OutOfRange_DoesNotThrow()
    {
        // No runtime narrowing means no runtime range check. 9999 stays a long.
        Assert.Equal(9999L, Run("let b: byte = 9999; let result = b;"));
    }

    [Fact]
    public void ByteAnnotation_IsLong_ReturnsTrue()
    {
        Assert.Equal(true, Run("let b: byte = 200; let result = b is int;"));
    }

    [Fact]
    public void ByteAnnotation_IsByte_ReturnsFalse()
    {
        Assert.Equal(false, Run("let b: byte = 200; let result = b is byte;"));
    }

    [Fact]
    public void ConvToByte_StillProducesByte()
    {
        // Migration path from `let b: byte = 200` is explicit conversion.
        Assert.Equal("byte", Run("let b = conv.toByte(200); let result = typeof(b);"));
    }

    // ── T[] element wrapping is removed: literal arrays stay plain arrays ───────

    [Fact]
    public void IntArrayAnnotation_LiteralIsPlainArray()
    {
        Assert.Equal("array", Run("let arr: int[] = [1, 2, 3]; let result = typeof(arr);"));
    }

    [Fact]
    public void IntArrayAnnotation_MixedTypes_DoesNotThrow()
    {
        // No element-type enforcement at construction; the array accepts anything.
        var result = (List<object?>)Run("let arr: int[] = [1, \"two\", 3.0]; let result = arr;")!;
        Assert.Equal(3, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal("two", result[1]);
        Assert.Equal(3.0, result[2]);
    }

    [Fact]
    public void IntArrayAnnotation_IsArray_ReturnsTrue()
    {
        Assert.Equal(true, Run("let arr: int[] = [1, 2, 3]; let result = arr is array;"));
    }

    [Fact]
    public void IntArrayAnnotation_IsIntArray_ReturnsFalse()
    {
        // Erasure: the annotation does not create element-type metadata, so the
        // value cannot satisfy `is int[]` even though the annotation says int[].
        Assert.Equal(false, Run("let arr: int[] = [1, 2, 3]; let result = arr is int[];"));
    }

    // ── Struct / arbitrary type annotations are not checked at runtime ─────────

    [Fact]
    public void StructAnnotation_WrongValue_DoesNotThrow()
    {
        // The analyzer warns (SA0301) but runtime accepts.
        Assert.Equal("string", Run("struct Point { x: int } let p: Point = \"not a point\"; let result = typeof(p);"));
    }

    [Fact]
    public void FunctionParameterAnnotation_WrongType_DoesNotThrow()
    {
        Assert.Equal("string", Run("fn take(x: int) { return typeof(x); } let result = take(\"hello\");"));
    }

    [Fact]
    public void FunctionReturnAnnotation_WrongType_DoesNotThrow()
    {
        Assert.Equal("string", Run("fn produce() -> int { return \"hello\"; } let result = typeof(produce());"));
    }
}
