namespace Stash.Tests.Stdlib.SourceGenerator;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using Stash.Tests.Stdlib.SourceGenerator.Fixtures;
using Xunit;

public class MarshalErrorTests
{
    private static readonly NamespaceDefinition _defn = MarshalFixture.Define();

    private static BuiltInFunction Get(string name)
    {
        var sv = _defn.Namespace.GetAllMemberValues()[name];
        return (BuiltInFunction)sv.ToObject()!;
    }

    private static StashValue Call(string name, params StashValue[] args)
        => Get(name).CallDirect(null!, args);

    [Fact]
    public void LongParam_NotInt_ThrowsTypeError()
    {
        var ex = Assert.Throws<TypeError>(() => Call("longParam", StashValue.FromObj("hi")));
        Assert.Contains("must be an int", ex.Message);
    }

    [Fact]
    public void DoubleParam_NotFloat_ThrowsTypeError()
    {
        var ex = Assert.Throws<TypeError>(() => Call("doubleParam", StashValue.FromObj("hi")));
        Assert.Contains("must be a float", ex.Message);
    }

    [Fact]
    public void StringParam_NotString_ThrowsTypeError()
    {
        var ex = Assert.Throws<TypeError>(() => Call("stringParam", StashValue.FromInt(1)));
        Assert.Contains("must be a string", ex.Message);
    }

    [Fact]
    public void BoolParam_NotBool_ThrowsTypeError()
    {
        var ex = Assert.Throws<TypeError>(() => Call("boolParam", StashValue.FromInt(1)));
        Assert.Contains("must be a bool", ex.Message);
    }

    [Fact]
    public void BufferParam_NotBuffer_ThrowsTypeError()
    {
        var ex = Assert.Throws<TypeError>(() => Call("bufferParam", StashValue.FromObj("hi")));
        Assert.Contains("must be a buffer", ex.Message);
    }

    [Fact]
    public void DictParam_NotDict_ThrowsTypeError()
    {
        var ex = Assert.Throws<TypeError>(() => Call("dictParam", StashValue.FromInt(1)));
        Assert.Contains("must be a dict", ex.Message);
    }

    [Fact]
    public void ListParam_NotArray_ThrowsTypeError()
    {
        var ex = Assert.Throws<TypeError>(() => Call("listParam", StashValue.FromInt(1)));
        Assert.Contains("must be a", ex.Message);
    }

    [Fact]
    public void LongParam_Int_Succeeds()
    {
        var result = Call("longParam", StashValue.FromInt(42));
        Assert.Equal(42L, result.AsInt);
    }

    [Fact]
    public void DoubleParam_Float_Succeeds()
    {
        var result = Call("doubleParam", StashValue.FromFloat(3.14));
        Assert.Equal(3.14, result.AsFloat);
    }

    [Fact]
    public void NumericParam_Int_AcceptsAndReturnsAsFloat()
    {
        var result = Call("numericParam", StashValue.FromInt(5));
        Assert.Equal(5.0, result.AsFloat);
    }

    [Fact]
    public void BoolParam_True_Succeeds()
    {
        var result = Call("boolParam", StashValue.FromBool(true));
        Assert.True(result.AsBool);
    }

    [Fact]
    public void StringParam_String_Succeeds()
    {
        var result = Call("stringParam", StashValue.FromObj("hi"));
        Assert.Equal("hi", result.ToObject());
    }

    [Fact]
    public void ByteParam_Byte_Succeeds()
    {
        var result = Call("byteParam", StashValue.FromByte(7));
        Assert.Equal((byte)7, (byte)result.AsByte);
    }

    [Fact]
    public void BufferParam_Buffer_Succeeds()
    {
        var ba = new StashByteArray(new byte[] { 1, 2, 3 });
        var result = Call("bufferParam", StashValue.FromObj(ba));
        Assert.Equal(3L, result.AsInt);
    }

    [Fact]
    public void DictParam_Dict_Succeeds()
    {
        var d = new StashDictionary();
        d.Set("a", StashValue.FromInt(1));
        var result = Call("dictParam", StashValue.FromObj(d));
        Assert.Equal(1L, result.AsInt);
    }

    [Fact]
    public void ListParam_Array_Succeeds()
    {
        var arr = new List<StashValue> { StashValue.FromInt(1), StashValue.FromInt(2) };
        var result = Call("listParam", StashValue.FromObj(arr));
        Assert.Equal(2L, result.AsInt);
    }

    [Fact]
    public void AnyParam_PassesThrough()
    {
        var v = StashValue.FromInt(99);
        var result = Call("anyParam", v);
        Assert.Equal(99L, result.AsInt);
    }

    [Fact]
    public void VoidReturn_Negative_ThrowsRuntimeError()
    {
        var ex = Assert.Throws<RuntimeError>(() => Call("voidReturn", StashValue.FromInt(-1)));
        Assert.Contains("negative", ex.Message);
    }

    [Fact]
    public void VoidReturn_Positive_ReturnsNull()
    {
        var result = Call("voidReturn", StashValue.FromInt(1));
        Assert.True(result.IsNull);
    }
}
