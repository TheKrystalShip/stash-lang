using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class DurationByteSizeTests
{
    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    // ── Duration Literal Parsing ──────────────────────────────────────────────

    [Fact]
    public void Duration_MillisecondUnit_ParsesCorrectly()
    {
        var result = Run("let result = 5ms;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(5L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_SecondUnit_ParsesCorrectly()
    {
        var result = Run("let result = 5s;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(5000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_MinuteUnit_ParsesCorrectly()
    {
        var result = Run("let result = 5m;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(300000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_HourUnit_ParsesCorrectly()
    {
        var result = Run("let result = 5h;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(18000000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_DayUnit_ParsesCorrectly()
    {
        var result = Run("let result = 5d;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(432000000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_FloatSeconds_ParsesCorrectly()
    {
        var result = Run("let result = 1.5s;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(1500L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_FloatHours_ParsesCorrectly()
    {
        var result = Run("let result = 0.5h;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(1800000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_FloatDays_ParsesCorrectly()
    {
        var result = Run("let result = 2.5d;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(216000000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_CompoundHoursMinutes_ParsesCorrectly()
    {
        var result = Run("let result = 2h30m;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(9000000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_CompoundHoursMinutesSeconds_ParsesCorrectly()
    {
        var result = Run("let result = 1h30m15s;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(5415000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_CompoundAllUnits_ParsesCorrectly()
    {
        var result = Run("let result = 2h30m15s500ms;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(9015500L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_CompoundDaysHours_ParsesCorrectly()
    {
        var result = Run("let result = 1d12h;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(129600000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_LargeValueDays_ParsesCorrectly()
    {
        var result = Run("let result = 365d;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(31536000000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_LargeValueMilliseconds_ParsesCorrectly()
    {
        var result = Run("let result = 1000000ms;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(1000000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_UnderscoreDigits_ParsesCorrectly()
    {
        var result = Run("let result = 1_000ms;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(1000L, dur.TotalMilliseconds);
    }

    // ── ByteSize Literal Parsing ──────────────────────────────────────────────

    [Fact]
    public void ByteSize_ByteUnit_ParsesCorrectly()
    {
        var result = Run("let result = 100B;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(100L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_KilobyteUnit_ParsesCorrectly()
    {
        var result = Run("let result = 1KB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(1024L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_MegabyteUnit_ParsesCorrectly()
    {
        var result = Run("let result = 1MB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(1048576L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_GigabyteUnit_ParsesCorrectly()
    {
        var result = Run("let result = 1GB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(1073741824L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_TerabyteUnit_ParsesCorrectly()
    {
        var result = Run("let result = 1TB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(1099511627776L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_FloatKilobytes_ParsesCorrectly()
    {
        var result = Run("let result = 1.5KB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(1536L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_FloatMegabytes_ParsesCorrectly()
    {
        var result = Run("let result = 2.5MB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(2621440L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_FloatGigabytes_ParsesCorrectly()
    {
        var result = Run("let result = 0.5GB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(536870912L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_UnderscoreDigits_ParsesCorrectly()
    {
        var result = Run("let result = 1_024KB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(1048576L, bs.TotalBytes);
    }

    // ── Duration Properties ───────────────────────────────────────────────────

    [Fact]
    public void Duration_TotalMs_ReturnsLong()
    {
        object? result = Run("let result = 5s.totalMs;");
        Assert.Equal(5000L, result);
    }

    [Fact]
    public void Duration_TotalSeconds_ReturnsDouble()
    {
        object? result = Run("let result = 5000ms.totalSeconds;");
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void Duration_TotalMinutes_ReturnsDouble()
    {
        object? result = Run("let result = 60s.totalMinutes;");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Duration_TotalHours_ReturnsDouble()
    {
        object? result = Run("let result = 3600s.totalHours;");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Duration_TotalDays_ReturnsDouble()
    {
        object? result = Run("let result = 86400s.totalDays;");
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Duration_MillisecondsComponent_ReturnsRemainder()
    {
        object? result = Run("let result = 1500ms.milliseconds;");
        Assert.Equal(500L, result);
    }

    [Fact]
    public void Duration_SecondsComponent_ReturnsRemainder()
    {
        object? result = Run("let result = 90s.seconds;");
        Assert.Equal(30L, result);
    }

    [Fact]
    public void Duration_MinutesComponent_ReturnsRemainder()
    {
        object? result = Run("let result = 90m.minutes;");
        Assert.Equal(30L, result);
    }

    [Fact]
    public void Duration_HoursComponent_ReturnsRemainder()
    {
        object? result = Run("let result = 25h.hours;");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Duration_DaysComponent_ReturnsFullDays()
    {
        object? result = Run("let result = 25h.days;");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Duration_CompoundTotalMinutes_CorrectValue()
    {
        object? result = Run("let result = 2h30m.totalMinutes;");
        Assert.Equal(150.0, result);
    }

    // ── ByteSize Properties ───────────────────────────────────────────────────

    [Fact]
    public void ByteSize_BytesProperty_ReturnsLong()
    {
        object? result = Run("let result = 1KB.bytes;");
        Assert.Equal(1024L, result);
    }

    [Fact]
    public void ByteSize_KbProperty_ReturnsDouble()
    {
        object? result = Run("let result = 1MB.kb;");
        Assert.Equal(1024.0, result);
    }

    [Fact]
    public void ByteSize_MbProperty_ReturnsDouble()
    {
        object? result = Run("let result = 1GB.mb;");
        Assert.Equal(1024.0, result);
    }

    [Fact]
    public void ByteSize_GbProperty_ReturnsDouble()
    {
        object? result = Run("let result = 1TB.gb;");
        Assert.Equal(1024.0, result);
    }

    [Fact]
    public void ByteSize_TbProperty_ReturnsDouble()
    {
        object? result = Run("let result = 1TB.tb;");
        Assert.Equal(1.0, result);
    }

    // ── Duration Arithmetic ───────────────────────────────────────────────────

    [Fact]
    public void Duration_Add_ReturnsSumDuration()
    {
        var result = Run("let result = 5s + 3s;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(8000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_Subtract_ReturnsDifferenceDuration()
    {
        var result = Run("let result = 10s - 3s;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(7000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_MultiplyByInt_ReturnsScaledDuration()
    {
        var result = Run("let result = 5s * 3;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(15000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_IntMultiply_IsCommutative()
    {
        var result = Run("let result = 3 * 5s;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(15000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_DivideByInt_ReturnsScaledDuration()
    {
        var result = Run("let result = 10s / 2;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(5000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_DivideByDuration_ReturnsRatio()
    {
        object? result = Run("let result = 10s / 5s;");
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void Duration_MultiplyByFloat_ReturnsScaledDuration()
    {
        var result = Run("let result = 5s * 1.5;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(7500L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_UnaryNegation_NegatesMilliseconds()
    {
        var result = Run("let result = -5s;");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(-5000L, dur.TotalMilliseconds);
    }

    // ── ByteSize Arithmetic ───────────────────────────────────────────────────

    [Fact]
    public void ByteSize_Add_ReturnsSumByteSize()
    {
        var result = Run("let result = 1KB + 1KB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(2048L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_Subtract_ReturnsDifferenceByteSize()
    {
        var result = Run("let result = 1MB - 512KB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(524288L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_MultiplyByInt_ReturnsScaledByteSize()
    {
        var result = Run("let result = 1KB * 3;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(3072L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_IntMultiply_IsCommutative()
    {
        var result = Run("let result = 3 * 1KB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(3072L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_DivideByInt_ReturnsScaledByteSize()
    {
        var result = Run("let result = 1MB / 2;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(524288L, bs.TotalBytes);
    }

    [Fact]
    public void ByteSize_DivideByByteSize_ReturnsRatio()
    {
        object? result = Run("let result = 1MB / 512KB;");
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void ByteSize_UnaryNegation_NegatesBytes()
    {
        var result = Run("let result = -1KB;");
        var bs = Assert.IsType<StashByteSize>(result);
        Assert.Equal(-1024L, bs.TotalBytes);
    }

    // ── Duration Comparisons ──────────────────────────────────────────────────

    [Fact]
    public void Duration_GreaterThan_ReturnsTrue()
    {
        object? result = Run("let result = 5s > 3s;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Duration_LessThan_ReturnsTrue()
    {
        object? result = Run("let result = 3s < 5s;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Duration_GreaterThanOrEqualToEqual_ReturnsTrue()
    {
        object? result = Run("let result = 5s >= 5s;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Duration_LessThanOrEqual_ReturnsTrue()
    {
        object? result = Run("let result = 3s <= 5s;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Duration_EqualSameValue_ReturnsTrue()
    {
        object? result = Run("let result = 5s == 5s;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Duration_NotEqualDifferentValues_ReturnsTrue()
    {
        object? result = Run("let result = 5s != 3s;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Duration_EqualEquivalentUnits_ReturnsTrue()
    {
        object? result = Run("let result = 1h == 60m;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Duration_GreaterThanCrossUnit_ReturnsTrue()
    {
        object? result = Run("let result = 1h > 59m;");
        Assert.Equal(true, result);
    }

    // ── ByteSize Comparisons ──────────────────────────────────────────────────

    [Fact]
    public void ByteSize_GreaterThan_ReturnsTrue()
    {
        object? result = Run("let result = 1MB > 1KB;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ByteSize_LessThan_ReturnsTrue()
    {
        object? result = Run("let result = 1KB < 1MB;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ByteSize_EqualEquivalentSizes_ReturnsTrue()
    {
        object? result = Run("let result = 1KB == 1024B;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ByteSize_NotEqualDifferentSizes_ReturnsTrue()
    {
        object? result = Run("let result = 1GB != 1MB;");
        Assert.Equal(true, result);
    }

    // ── typeof and is ─────────────────────────────────────────────────────────

    [Fact]
    public void Duration_TypeOf_ReturnsDurationString()
    {
        object? result = Run("let result = typeof(5s);");
        Assert.Equal("duration", result);
    }

    [Fact]
    public void ByteSize_TypeOf_ReturnsBytesString()
    {
        object? result = Run("let result = typeof(1KB);");
        Assert.Equal("bytes", result);
    }

    [Fact]
    public void Duration_IsDuration_ReturnsTrue()
    {
        object? result = Run("let result = 5s is duration;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ByteSize_IsBytes_ReturnsTrue()
    {
        object? result = Run("let result = 1KB is bytes;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Duration_IsInt_ReturnsFalse()
    {
        object? result = Run("let result = 5s is int;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Int_IsDuration_ReturnsFalse()
    {
        object? result = Run("let result = 5 is duration;");
        Assert.Equal(false, result);
    }

    // ── Stringify ─────────────────────────────────────────────────────────────

    [Fact]
    public void Duration_Stringify_SimpleSeconds()
    {
        object? result = Run("let result = \"\" + 5s;");
        Assert.Equal("5s", result);
    }

    [Fact]
    public void Duration_Stringify_CompoundHoursMinutes()
    {
        object? result = Run("let result = \"\" + 2h30m;");
        Assert.Equal("2h30m", result);
    }

    [Fact]
    public void Duration_Stringify_Milliseconds()
    {
        object? result = Run("let result = \"\" + 500ms;");
        Assert.Equal("500ms", result);
    }

    [Fact]
    public void ByteSize_Stringify_RoundKilobytes()
    {
        object? result = Run("let result = \"\" + 1KB;");
        Assert.Equal("1KB", result);
    }

    [Fact]
    public void ByteSize_Stringify_AutoScalesToKilobytes()
    {
        object? result = Run("let result = \"\" + 1536B;");
        Assert.Equal("1.5KB", result);
    }

    // ── Error Cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Duration_AddByteSize_ThrowsError()
    {
        Assert.ThrowsAny<Exception>(() => Run("let result = 5s + 1KB;"));
    }

    [Fact]
    public void Duration_CompareToByteSize_ThrowsError()
    {
        Assert.ThrowsAny<Exception>(() => Run("let result = 5s > 1KB;"));
    }

    [Fact]
    public void Duration_UnknownField_ThrowsError()
    {
        Assert.ThrowsAny<Exception>(() => Run("let result = 5s.nonexistent;"));
    }

    [Fact]
    public void ByteSize_UnknownField_ThrowsError()
    {
        Assert.ThrowsAny<Exception>(() => Run("let result = 1KB.nonexistent;"));
    }

    // ── Edge Cases (Review Findings) ──────────────────────────────────────────

    [Fact]
    public void Duration_ZeroMs_Parses()
    {
        object? result = Run("let result = 0ms.totalMs;");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Duration_ZeroSeconds_Parses()
    {
        object? result = Run("let result = 0s.totalMs;");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void ByteSize_ZeroByte_Parses()
    {
        object? result = Run("let result = 0B.bytes;");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void ByteSize_ZeroKB_Parses()
    {
        object? result = Run("let result = 0KB.bytes;");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Duration_DoubleNegation_Works()
    {
        object? result = Run("let d = 5s; let result = -(-d);");
        var dur = Assert.IsType<StashDuration>(result);
        Assert.Equal(5000L, dur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_DivideByZero_ThrowsError()
    {
        Assert.ThrowsAny<Exception>(() => Run("let result = 5s / 0;"));
    }

    [Fact]
    public void Duration_DivideByZeroDuration_ThrowsError()
    {
        Assert.ThrowsAny<Exception>(() => Run("let result = 5s / 0s;"));
    }

    [Fact]
    public void ByteSize_DivideByZero_ThrowsError()
    {
        Assert.ThrowsAny<Exception>(() => Run("let result = 1KB / 0;"));
    }

    [Fact]
    public void ByteSize_DivideByZeroByteSize_ThrowsError()
    {
        Assert.ThrowsAny<Exception>(() => Run("let result = 1KB / 0KB;"));
    }

    [Fact]
    public void ByteSize_LessOrEqual_ReturnsTrue()
    {
        object? result = Run("let result = 1KB <= 1MB;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ByteSize_GreaterOrEqual_ReturnsTrue()
    {
        object? result = Run("let result = 1MB >= 1KB;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ByteSize_CrossTypeSub_ThrowsError()
    {
        Assert.ThrowsAny<Exception>(() => Run("let result = 1KB - 5s;"));
    }

    [Fact]
    public void BinaryLiteral_0B1010_StillWorks()
    {
        object? result = Run("let result = 0B1010;");
        Assert.Equal(10L, result);
    }
}
