namespace Stash.Tests.Bytecode;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;
using Stash.Runtime.Types;

public class TypeProtocolTests
{
    // ======================= IVMTyped =======================

    [Theory]
    [InlineData(typeof(StashDuration), "duration")]
    [InlineData(typeof(StashByteSize), "bytes")]
    [InlineData(typeof(StashSemVer), "semver")]
    [InlineData(typeof(StashIpAddress), "ip")]
    [InlineData(typeof(StashSecret), "secret")]
    [InlineData(typeof(StashError), "Error")]
    [InlineData(typeof(StashRange), "range")]
    [InlineData(typeof(StashFuture), "Future")]
    [InlineData(typeof(StashStruct), "struct")]
    [InlineData(typeof(StashEnum), "enum")]
    [InlineData(typeof(StashNamespace), "namespace")]
    [InlineData(typeof(StashDictionary), "dict")]
    public void VMTypeName_ReturnsExpectedName(Type type, string expected)
    {
        object instance = CreateInstance(type);
        string typeName = ((IVMTyped)instance).VMTypeName;
        Assert.Equal(expected, typeName);
    }

    [Fact]
    public void VMTypeName_StashInstance_ReturnsStructName()
    {
        var structDef = new StashStruct("Point", new List<string> { "x", "y" }, new Dictionary<string, IStashCallable>());
        var instance = new StashInstance("Point", structDef, new StashValue[2]);
        Assert.Equal("Point", ((IVMTyped)instance).VMTypeName);
    }

    [Fact]
    public void VMTypeName_StashEnumValue_ReturnsEnumName()
    {
        var ev = new StashEnumValue("Color", "Red");
        Assert.Equal("Color", ((IVMTyped)ev).VMTypeName);
    }

    [Fact]
    public void VMTypeName_StashIntArray_ReturnsIntArray()
    {
        var arr = StashTypedArray.Create("int", new List<StashValue> { StashValue.FromInt(1) });
        Assert.Equal("int[]", ((IVMTyped)arr).VMTypeName);
    }

    // ======================= IVMFieldAccessible =======================

    [Fact]
    public void Duration_VMTryGetField_TotalMs()
    {
        var dur = new StashDuration(5000); // 5 seconds
        bool found = ((IVMFieldAccessible)dur).VMTryGetField("totalMs", out StashValue value, null);
        Assert.True(found);
        Assert.Equal(5000L, value.AsInt);
    }

    [Fact]
    public void Duration_VMTryGetField_Seconds()
    {
        var dur = new StashDuration(65000); // 1 minute 5 seconds
        bool found = ((IVMFieldAccessible)dur).VMTryGetField("seconds", out StashValue value, null);
        Assert.True(found);
        Assert.Equal(5L, value.AsInt);
    }

    [Fact]
    public void Duration_VMTryGetField_UnknownField_ReturnsFalse()
    {
        var dur = new StashDuration(5000);
        bool found = ((IVMFieldAccessible)dur).VMTryGetField("unknown", out _, null);
        Assert.False(found);
    }

    [Fact]
    public void ByteSize_VMTryGetField_Bytes()
    {
        var bs = new StashByteSize(1024);
        bool found = ((IVMFieldAccessible)bs).VMTryGetField("bytes", out StashValue value, null);
        Assert.True(found);
        Assert.Equal(1024L, value.AsInt);
    }

    [Fact]
    public void ByteSize_VMTryGetField_Kb()
    {
        var bs = new StashByteSize(1024);
        bool found = ((IVMFieldAccessible)bs).VMTryGetField("kb", out StashValue value, null);
        Assert.True(found);
        Assert.Equal(1.0, value.AsFloat);
    }

    [Fact]
    public void SemVer_VMTryGetField_Major()
    {
        StashSemVer.TryParse("3.2.1", out StashSemVer? sv);
        bool found = ((IVMFieldAccessible)sv!).VMTryGetField("major", out StashValue value, null);
        Assert.True(found);
        Assert.Equal(3L, value.AsInt);
    }

    [Fact]
    public void SemVer_VMTryGetField_Prerelease_Default()
    {
        StashSemVer.TryParse("1.0.0", out StashSemVer? sv);
        bool found = ((IVMFieldAccessible)sv!).VMTryGetField("prerelease", out StashValue value, null);
        Assert.True(found);
        Assert.Equal("", value.AsObj);
    }

    [Fact]
    public void IpAddress_VMTryGetField_Version()
    {
        StashIpAddress.TryParse("192.168.1.1", out StashIpAddress? ip);
        bool found = ((IVMFieldAccessible)ip!).VMTryGetField("version", out StashValue value, null);
        Assert.True(found);
        Assert.Equal(4L, value.AsInt);
    }

    [Fact]
    public void IpAddress_VMTryGetField_IsLoopback()
    {
        StashIpAddress.TryParse("127.0.0.1", out StashIpAddress? ip);
        bool found = ((IVMFieldAccessible)ip!).VMTryGetField("isLoopback", out StashValue value, null);
        Assert.True(found);
        Assert.True(value.AsBool);
    }

    [Fact]
    public void Error_VMTryGetField_Message()
    {
        var error = new StashError("something broke", "RuntimeError");
        bool found = ((IVMFieldAccessible)error).VMTryGetField("message", out StashValue value, null);
        Assert.True(found);
        Assert.Equal("something broke", value.AsObj);
    }

    [Fact]
    public void Error_VMTryGetField_Type()
    {
        var error = new StashError("something broke", "TypeError");
        bool found = ((IVMFieldAccessible)error).VMTryGetField("type", out StashValue value, null);
        Assert.True(found);
        Assert.Equal("TypeError", value.AsObj);
    }

    [Fact]
    public void Instance_VMTryGetField_ExistingField()
    {
        var structDef = new StashStruct("Point", new List<string> { "x", "y" }, new Dictionary<string, IStashCallable>());
        var instance = new StashInstance("Point", structDef, new StashValue[2]);
        instance.SetField("x", StashValue.FromInt(42), null);
        bool found = ((IVMFieldAccessible)instance).VMTryGetField("x", out StashValue value, null);
        Assert.True(found);
        Assert.Equal(42L, value.AsInt);
    }

    [Fact]
    public void Instance_VMTryGetField_UnknownField_ReturnsFalse()
    {
        var structDef = new StashStruct("Point", new List<string> { "x" }, new Dictionary<string, IStashCallable>());
        var instance = new StashInstance("Point", structDef, new StashValue[1]);
        bool found = ((IVMFieldAccessible)instance).VMTryGetField("z", out _, null);
        Assert.False(found);
    }

    [Fact]
    public void Dict_VMTryGetField_ExistingKey()
    {
        var dict = new StashDictionary();
        dict.Set("key", StashValue.FromInt(99));
        bool found = ((IVMFieldAccessible)dict).VMTryGetField("key", out StashValue value, null);
        Assert.True(found);
        Assert.Equal(99L, value.AsInt);
    }

    [Fact]
    public void Dict_VMTryGetField_MissingKey_ReturnsFalse()
    {
        var dict = new StashDictionary();
        bool found = ((IVMFieldAccessible)dict).VMTryGetField("missing", out _, null);
        Assert.False(found);
    }

    [Fact]
    public void Struct_VMTryGetField_UnknownMethod_ReturnsFalse()
    {
        var structDef = new StashStruct("Foo", new List<string>(), new Dictionary<string, IStashCallable>());
        bool found = ((IVMFieldAccessible)structDef).VMTryGetField("bar", out _, null);
        Assert.False(found);
    }

    [Fact]
    public void Enum_VMTryGetField_Member()
    {
        var enumDef = new StashEnum("Color", new List<string> { "Red", "Green", "Blue" });
        bool found = ((IVMFieldAccessible)enumDef).VMTryGetField("Red", out StashValue value, null);
        Assert.True(found);
        Assert.IsType<StashEnumValue>(value.AsObj);
    }

    [Fact]
    public void EnumValue_VMTryGetField_TypeName()
    {
        var ev = new StashEnumValue("Color", "Red");
        bool found = ((IVMFieldAccessible)ev).VMTryGetField("typeName", out StashValue value, null);
        Assert.True(found);
        Assert.Equal("Color", value.AsObj);
    }

    [Fact]
    public void EnumValue_VMTryGetField_MemberName()
    {
        var ev = new StashEnumValue("Color", "Red");
        bool found = ((IVMFieldAccessible)ev).VMTryGetField("memberName", out StashValue value, null);
        Assert.True(found);
        Assert.Equal("Red", value.AsObj);
    }

    // ======================= IVMArithmetic =======================

    [Fact]
    public void Duration_Add_Duration()
    {
        var dur1 = new StashDuration(5000);
        var dur2 = new StashDuration(3000);
        bool ok = ((IVMArithmetic)dur1).VMTryArithmetic(ArithmeticOp.Add, StashValue.FromObj(dur2), true, out StashValue result, null);
        Assert.True(ok);
        var resultDur = Assert.IsType<StashDuration>(result.AsObj);
        Assert.Equal(8000L, resultDur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_Multiply_Int()
    {
        var dur = new StashDuration(5000);
        bool ok = ((IVMArithmetic)dur).VMTryArithmetic(ArithmeticOp.Multiply, StashValue.FromInt(3), true, out StashValue result, null);
        Assert.True(ok);
        var resultDur = Assert.IsType<StashDuration>(result.AsObj);
        Assert.Equal(15000L, resultDur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_Multiply_ReverseDispatch()
    {
        var dur = new StashDuration(5000);
        // 3 * dur — dur is right operand, isLeftOperand=false
        bool ok = ((IVMArithmetic)dur).VMTryArithmetic(ArithmeticOp.Multiply, StashValue.FromInt(3), false, out StashValue result, null);
        Assert.True(ok);
        var resultDur = Assert.IsType<StashDuration>(result.AsObj);
        Assert.Equal(15000L, resultDur.TotalMilliseconds);
    }

    [Fact]
    public void Duration_Add_NonDuration_ReturnsFalse()
    {
        var dur = new StashDuration(5000);
        bool ok = ((IVMArithmetic)dur).VMTryArithmetic(ArithmeticOp.Add, StashValue.FromInt(5), true, out _, null);
        Assert.False(ok);
    }

    [Fact]
    public void Duration_Negate()
    {
        var dur = new StashDuration(5000);
        bool ok = ((IVMArithmetic)dur).VMTryArithmetic(ArithmeticOp.Negate, StashValue.Null, true, out StashValue result, null);
        Assert.True(ok);
        var resultDur = Assert.IsType<StashDuration>(result.AsObj);
        Assert.Equal(-5000L, resultDur.TotalMilliseconds);
    }

    [Fact]
    public void ByteSize_Add_ByteSize()
    {
        var bs1 = new StashByteSize(1024);
        var bs2 = new StashByteSize(2048);
        bool ok = ((IVMArithmetic)bs1).VMTryArithmetic(ArithmeticOp.Add, StashValue.FromObj(bs2), true, out StashValue result, null);
        Assert.True(ok);
        var resultBs = Assert.IsType<StashByteSize>(result.AsObj);
        Assert.Equal(3072L, resultBs.TotalBytes);
    }

    [Fact]
    public void IpAddress_Add_Int()
    {
        StashIpAddress.TryParse("192.168.1.1", out StashIpAddress? ip);
        bool ok = ((IVMArithmetic)ip!).VMTryArithmetic(ArithmeticOp.Add, StashValue.FromInt(1), true, out StashValue result, null);
        Assert.True(ok);
        var resultIp = Assert.IsType<StashIpAddress>(result.AsObj);
        Assert.Equal("192.168.1.2", resultIp.ToString());
    }

    [Fact]
    public void IpAddress_Add_Int_ReverseDispatch()
    {
        StashIpAddress.TryParse("192.168.1.1", out StashIpAddress? ip);
        // int + ip — ip is right, isLeftOperand=false
        bool ok = ((IVMArithmetic)ip!).VMTryArithmetic(ArithmeticOp.Add, StashValue.FromInt(1), false, out StashValue result, null);
        Assert.True(ok);
        var resultIp = Assert.IsType<StashIpAddress>(result.AsObj);
        Assert.Equal("192.168.1.2", resultIp.ToString());
    }

    [Fact]
    public void Secret_Add_TaintPropagation()
    {
        var secret = new StashSecret(StashValue.FromObj("password"));
        bool ok = ((IVMArithmetic)secret).VMTryArithmetic(ArithmeticOp.Add, StashValue.FromObj("_suffix"), true, out StashValue result, null);
        Assert.True(ok);
        var resultSecret = Assert.IsType<StashSecret>(result.AsObj);
        Assert.Equal("password_suffix", resultSecret.Reveal().AsObj);
    }

    // ======================= IVMComparable =======================

    [Fact]
    public void Duration_Compare_Less()
    {
        var dur1 = new StashDuration(3000);
        var dur2 = new StashDuration(5000);
        bool ok = ((IVMComparable)dur1).VMTryCompare(StashValue.FromObj(dur2), out int result, null);
        Assert.True(ok);
        Assert.True(result < 0);
    }

    [Fact]
    public void SemVer_Compare_GreaterMajor()
    {
        StashSemVer.TryParse("2.0.0", out StashSemVer? sv1);
        StashSemVer.TryParse("1.9.9", out StashSemVer? sv2);
        bool ok = ((IVMComparable)sv1!).VMTryCompare(StashValue.FromObj(sv2!), out int result, null);
        Assert.True(ok);
        Assert.True(result > 0);
    }

    [Fact]
    public void IpAddress_Compare()
    {
        StashIpAddress.TryParse("192.168.1.2", out StashIpAddress? ip1);
        StashIpAddress.TryParse("192.168.1.1", out StashIpAddress? ip2);
        bool ok = ((IVMComparable)ip1!).VMTryCompare(StashValue.FromObj(ip2!), out int result, null);
        Assert.True(ok);
        Assert.True(result > 0);
    }

    [Fact]
    public void Duration_Compare_NonDuration_ReturnsFalse()
    {
        var dur = new StashDuration(5000);
        bool ok = ((IVMComparable)dur).VMTryCompare(StashValue.FromInt(5), out _, null);
        Assert.False(ok);
    }

    // ======================= IVMEquatable =======================

    [Fact]
    public void EnumValue_VMEquals_SameValue()
    {
        var ev1 = new StashEnumValue("Color", "Red");
        var ev2 = new StashEnumValue("Color", "Red");
        Assert.True(((IVMEquatable)ev1).VMEquals(StashValue.FromObj(ev2)));
    }

    [Fact]
    public void EnumValue_VMEquals_DifferentValue()
    {
        var ev1 = new StashEnumValue("Color", "Red");
        var ev2 = new StashEnumValue("Color", "Blue");
        Assert.False(((IVMEquatable)ev1).VMEquals(StashValue.FromObj(ev2)));
    }

    // ======================= IVMTruthiness =======================

    [Fact]
    public void Error_IsFalsy()
    {
        var error = new StashError("msg", "RuntimeError");
        Assert.True(((IVMTruthiness)error).VMIsFalsy);
    }

    [Fact]
    public void Secret_IsNotFalsy()
    {
        var secret = new StashSecret(StashValue.FromObj("val"));
        Assert.False(((IVMTruthiness)secret).VMIsFalsy);
    }

    // ======================= IVMIterable =======================

    [Fact]
    public void Range_Iterate()
    {
        var range = new StashRange(0, 5, 1);
        var iterator = ((IVMIterable)range).VMGetIterator(false);
        var values = new List<long>();
        while (iterator.MoveNext())
            values.Add(iterator.Current.AsInt);
        Assert.Equal(new List<long> { 0, 1, 2, 3, 4 }, values);
    }

    [Fact]
    public void Range_Iterate_WithStep()
    {
        var range = new StashRange(0, 10, 3);
        var iterator = ((IVMIterable)range).VMGetIterator(false);
        var values = new List<long>();
        while (iterator.MoveNext())
            values.Add(iterator.Current.AsInt);
        Assert.Equal(new List<long> { 0, 3, 6, 9 }, values);
    }

    [Fact]
    public void Range_Iterate_Indexed()
    {
        var range = new StashRange(10, 13, 1);
        var iterator = ((IVMIterable)range).VMGetIterator(true);
        Assert.True(iterator.MoveNext());
        Assert.Equal(10L, iterator.Current.AsInt);
        Assert.Equal(0L, iterator.CurrentKey.AsInt);
    }

    [Fact]
    public void Dict_Iterate()
    {
        var dict = new StashDictionary();
        dict.Set("a", StashValue.FromInt(1));
        dict.Set("b", StashValue.FromInt(2));
        var iterator = ((IVMIterable)dict).VMGetIterator(false);
        int count = 0;
        while (iterator.MoveNext())
            count++;
        Assert.Equal(2, count);
    }

    [Fact]
    public void Enum_Iterate()
    {
        var enumDef = new StashEnum("Color", new List<string> { "Red", "Green", "Blue" });
        var iterator = ((IVMIterable)enumDef).VMGetIterator(false);
        var values = new List<string>();
        while (iterator.MoveNext())
        {
            var ev = Assert.IsType<StashEnumValue>(iterator.Current.AsObj);
            values.Add(ev.MemberName);
        }
        Assert.Equal(new List<string> { "Red", "Green", "Blue" }, values);
    }

    [Fact]
    public void TypedArray_Iterate()
    {
        var arr = StashTypedArray.Create("int", new List<StashValue>
        {
            StashValue.FromInt(10), StashValue.FromInt(20), StashValue.FromInt(30)
        });
        var iterator = ((IVMIterable)arr).VMGetIterator(false);
        var values = new List<long>();
        while (iterator.MoveNext())
            values.Add(iterator.Current.AsInt);
        Assert.Equal(new List<long> { 10, 20, 30 }, values);
    }

    // ======================= IVMIndexable =======================

    [Fact]
    public void Dict_VMGetIndex()
    {
        var dict = new StashDictionary();
        dict.Set("key", StashValue.FromInt(42));
        StashValue result = ((IVMIndexable)dict).VMGetIndex(StashValue.FromObj("key"), null);
        Assert.Equal(42L, result.AsInt);
    }

    [Fact]
    public void TypedArray_VMGetIndex()
    {
        var arr = StashTypedArray.Create("int", new List<StashValue> { StashValue.FromInt(5), StashValue.FromInt(10) });
        StashValue result = ((IVMIndexable)arr).VMGetIndex(StashValue.FromInt(1), null);
        Assert.Equal(10L, result.AsInt);
    }

    [Fact]
    public void TypedArray_VMGetIndex_NegativeIndex()
    {
        var arr = StashTypedArray.Create("int", new List<StashValue> { StashValue.FromInt(5), StashValue.FromInt(10) });
        StashValue result = ((IVMIndexable)arr).VMGetIndex(StashValue.FromInt(-1), null);
        Assert.Equal(10L, result.AsInt);
    }

    // ======================= IVMSized =======================

    [Fact]
    public void Dict_VMLength()
    {
        var dict = new StashDictionary();
        dict.Set("a", StashValue.FromInt(1));
        dict.Set("b", StashValue.FromInt(2));
        Assert.Equal(2L, ((IVMSized)dict).VMLength);
    }

    [Fact]
    public void TypedArray_VMLength()
    {
        var arr = StashTypedArray.Create("int", new List<StashValue> { StashValue.FromInt(1), StashValue.FromInt(2), StashValue.FromInt(3) });
        Assert.Equal(3L, ((IVMSized)arr).VMLength);
    }

    // ======================= IVMStringifiable =======================

    [Fact]
    public void Duration_VMToString()
    {
        var dur = new StashDuration(3600000); // 1 hour
        Assert.Equal("1h", ((IVMStringifiable)dur).VMToString());
    }

    [Fact]
    public void Secret_VMToString_Redacted()
    {
        var secret = new StashSecret(StashValue.FromObj("password"));
        Assert.Equal("******", ((IVMStringifiable)secret).VMToString());
    }

    [Fact]
    public void Error_VMToString()
    {
        var error = new StashError("not found", "NotFoundError");
        Assert.Equal("NotFoundError: not found", ((IVMStringifiable)error).VMToString());
    }

    // ======================= Helper =======================

    private static object CreateInstance(Type type)
    {
        if (type == typeof(StashDuration)) return new StashDuration(5000);
        if (type == typeof(StashByteSize)) return new StashByteSize(1024);
        if (type == typeof(StashSemVer)) { StashSemVer.TryParse("1.0.0", out StashSemVer? sv); return sv!; }
        if (type == typeof(StashIpAddress)) { StashIpAddress.TryParse("127.0.0.1", out StashIpAddress? ip); return ip!; }
        if (type == typeof(StashSecret)) return new StashSecret(StashValue.FromObj("secret"));
        if (type == typeof(StashError)) return new StashError("msg", "RuntimeError");
        if (type == typeof(StashRange)) return new StashRange(0, 10, 1);
        if (type == typeof(StashFuture)) return StashFuture.Resolved(null);
        if (type == typeof(StashStruct)) return new StashStruct("Test", new List<string>(), new Dictionary<string, IStashCallable>());
        if (type == typeof(StashEnum)) return new StashEnum("TestEnum", new List<string> { "A" });
        if (type == typeof(StashNamespace)) return new StashNamespace("test");
        if (type == typeof(StashDictionary)) return new StashDictionary();
        throw new ArgumentException($"Unknown type: {type}");
    }
}
