using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Array, dictionary, struct, destructure, and iterator opcode handlers.
/// </summary>
public sealed partial class VirtualMachine
{
    private object? GetIndexValue(object? obj, object? index, ref CallFrame frame)
    {
        if (obj is List<StashValue> svList)
        {
            if (index is not long i)
            {
                throw new RuntimeError("Array index must be an integer.", GetCurrentSpan(ref frame));
            }
            if (i < 0) i += svList.Count;
            if (i < 0 || i >= svList.Count)
            {
                throw new RuntimeError($"Index {index} out of bounds for array of length {svList.Count}.", GetCurrentSpan(ref frame));
            }
            return svList[(int)i].ToObject();
        }
        if (obj is StashDictionary dict)
        {
            if (index is null)
            {
                throw new RuntimeError("Dictionary key cannot be null.", GetCurrentSpan(ref frame));
            }

            return dict.Get(index).ToObject();
        }

        if (obj is string s)
        {
            if (index is not long idx)
            {
                throw new RuntimeError("String index must be an integer.", GetCurrentSpan(ref frame));
            }

            if (idx < 0)
            {
                idx += s.Length;
            }

            if (idx < 0 || idx >= s.Length)
            {
                throw new RuntimeError($"Index {index} out of bounds for string of length {s.Length}.", GetCurrentSpan(ref frame));
            }

            return s[(int)idx].ToString();
        }
        throw new RuntimeError($"Cannot index into {RuntimeValues.Stringify(obj)}.", GetCurrentSpan(ref frame));
    }

    private void SetIndexValue(object? obj, object? index, object? value, ref CallFrame frame)
    {
        if (obj is List<StashValue> svList)
        {
            if (index is not long i)
            {
                throw new RuntimeError("Array index must be an integer.", GetCurrentSpan(ref frame));
            }
            if (i < 0) i += svList.Count;
            if (i < 0 || i >= svList.Count)
            {
                throw new RuntimeError($"Index {index} out of bounds for array of length {svList.Count}.", GetCurrentSpan(ref frame));
            }
            svList[(int)i] = StashValue.FromObject(value);
            return;
        }
        if (obj is StashDictionary dict)
        {
            if (index is null)
            {
                throw new RuntimeError("Dictionary key cannot be null.", GetCurrentSpan(ref frame));
            }

            dict.Set(index, StashValue.FromObject(value));
            return;
        }
        throw new RuntimeError($"Cannot index-assign into {RuntimeValues.Stringify(obj)}.", GetCurrentSpan(ref frame));
    }

    private void ExecuteGetTable(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue obj = _stack[@base + b];
        StashValue idx = _stack[@base + c];

        // Fast path: array[int]
        if (obj.Tag == StashValueTag.Obj && obj.AsObj is List<StashValue> list && idx.IsInt)
        {
            long i = idx.AsInt;
            if (i < 0) i += list.Count;
            if ((ulong)i < (ulong)list.Count)
            {
                _stack[@base + a] = list[(int)i];
                return;
            }
            throw new RuntimeError(
                $"Index {idx.AsInt} out of bounds for array of length {list.Count}.",
                GetCurrentSpan(ref frame));
        }

        // General path
        _stack[@base + a] = StashValue.FromObject(GetIndexValue(obj.ToObject(), idx.ToObject(), ref frame));
    }

    private void ExecuteSetTable(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue obj = _stack[@base + a];
        StashValue idx = _stack[@base + b];
        StashValue val = _stack[@base + c];

        // Fast path: array[int] = val
        if (obj.Tag == StashValueTag.Obj && obj.AsObj is List<StashValue> list && idx.IsInt)
        {
            long i = idx.AsInt;
            if (i < 0) i += list.Count;
            if ((ulong)i < (ulong)list.Count)
            {
                list[(int)i] = val;
                return;
            }
            throw new RuntimeError(
                $"Index {idx.AsInt} out of bounds for array of length {list.Count}.",
                GetCurrentSpan(ref frame));
        }

        SetIndexValue(obj.ToObject(), idx.ToObject(), val.ToObject(), ref frame);
    }

    private void ExecuteGetField(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        string fieldName = (string)frame.Chunk.Constants[c].AsObj!;
        StashValue objVal = _stack[@base + b];

        // Fast path: namespace member access (e.g., math.abs)
        if (objVal.Tag == StashValueTag.Obj && objVal.AsObj is StashNamespace ns)
        {
            _stack[@base + a] = ns.GetMemberValue(fieldName, null);
            return;
        }

        object? obj = objVal.ToObject();
        object? result = GetFieldValue(obj, fieldName, GetCurrentSpan(ref frame));
        // Convert StashBoundMethod to VMBoundMethod for in-VM method dispatch
        if (result is StashBoundMethod bound && bound.Method is VMFunction vmFunc)
            result = new VMBoundMethod(bound.Instance, vmFunc);
        _stack[@base + a] = StashValue.FromObject(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteGetFieldIC(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte c = Instruction.GetC(inst);
        int icIdx = (int)frame.Chunk.Code[frame.IP++]; // read companion word
        int @base = frame.BaseSlot;

        ref ICSlot ic = ref frame.Chunk.ICSlots![icIdx];
        StashValue objVal = _stack[@base + b];

        // IC fast path: monomorphic hit
        if (ic.State == 1)
        {
            // Namespace IC hit: guard is namespace reference
            if (objVal.AsObj is StashNamespace && objVal.AsObj == ic.Guard)
            {
                _stack[@base + a] = ic.CachedValue;
                return;
            }

            // Struct field IC hit: guard is StashStruct reference
            if (objVal.AsObj is StashInstance si && si.Struct == ic.Guard)
            {
                _stack[@base + a] = si.FieldSlots![(int)ic.CachedValue.AsInt];
                return;
            }

            // Guard mismatch → megamorphic
            ic.State = 2;
        }

        ExecuteGetFieldICSlow(ref frame, a, b, c, icIdx, @base, objVal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteGetFieldICSlow(ref CallFrame frame, byte a, byte b, byte c, int icIdx, int @base, StashValue objVal)
    {
        ref ICSlot ic = ref frame.Chunk.ICSlots![icIdx];

        // IC slow path: full lookup + populate/transition
        string fieldName = (string)frame.Chunk.Constants[c].AsObj!;

        if (objVal.Tag == StashValueTag.Obj)
        {
            object? rawObj = objVal.AsObj;

            // Namespace member access
            if (rawObj is StashNamespace ns)
            {
                StashValue result = ns.GetMemberValue(fieldName, null);
                _stack[@base + a] = result;

                if (ns.IsFrozen)
                {
                    if (ic.State == 0)
                    {
                        ic.Guard = ns;
                        ic.CachedValue = result;
                        ic.State = 1;
                    }
                    else if (ic.State == 1)
                    {
                        ic.State = 2;
                    }
                }
                return;
            }

            // Struct field access
            if (rawObj is StashInstance inst2 && inst2.FieldSlots is not null && inst2.Struct is not null)
            {
                StashValue result = inst2.GetField(fieldName, GetCurrentSpan(ref frame));
                // Convert StashBoundMethod to VMBoundMethod for in-VM method dispatch
                if (result.AsObj is StashBoundMethod bound2 && bound2.Method is VMFunction vmFunc2)
                    result = StashValue.FromObj(new VMBoundMethod(bound2.Instance, vmFunc2));
                _stack[@base + a] = result;

                if (ic.State == 0 && inst2.Struct.FieldIndices.TryGetValue(fieldName, out int fieldIdx))
                {
                    ic.Guard = inst2.Struct;
                    ic.CachedValue = StashValue.FromInt(fieldIdx);
                    ic.State = 1;
                }
                else if (ic.State <= 1)
                {
                    ic.State = 2;
                }
                return;
            }
        }

        // General fallback (non-namespace, non-slot-struct)
        object? obj = objVal.ToObject();
        object? result2 = GetFieldValue(obj, fieldName, GetCurrentSpan(ref frame));
        if (result2 is StashBoundMethod bound && bound.Method is VMFunction vmFunc)
            result2 = new VMBoundMethod(bound.Instance, vmFunc);
        _stack[@base + a] = StashValue.FromObject(result2);

        // Transition IC to megamorphic for non-optimized receiver types
        if (ic.State <= 1) ic.State = 2;
    }

    private void ExecuteSetField(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        string fieldName = (string)frame.Chunk.Constants[b].AsObj!;
        object? obj = _stack[@base + a].ToObject();
        object? value = _stack[@base + c].ToObject();
        SetFieldValue(obj, fieldName, value, GetCurrentSpan(ref frame));
    }

    private void ExecuteSelf(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        string methodName = (string)frame.Chunk.Constants[c].AsObj!;

        // Copy receiver to R(A+1), look up method and store in R(A).
        _stack[@base + a + 1] = _stack[@base + b];
        object? receiver = _stack[@base + b].ToObject();
        object? method = GetFieldValue(receiver, methodName, GetCurrentSpan(ref frame));
        // Unwrap bound methods — Self already provides the receiver at R(A+1).
        if (method is VMBoundMethod vmBound) method = vmBound.Function;
        else if (method is StashBoundMethod stashBound && stashBound.Method is VMFunction vmFn) method = vmFn;
        _stack[@base + a] = StashValue.FromObject(method);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteNewArray(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte count = Instruction.GetB(inst);
        int @base = frame.BaseSlot;
        var list = new List<StashValue>(count);
        for (int i = 0; i < count; i++)
        {
            StashValue val = _stack[@base + a + 1 + i];
            if (val.IsObj && val.AsObj is SpreadMarker sm)
            {
                if (sm.Items is List<StashValue> items)
                    list.AddRange(items);
                else
                    throw new RuntimeError("Spread operator requires an array.", GetCurrentSpan(ref frame));
            }
            else
            {
                list.Add(val);
            }
        }
        _stack[@base + a] = StashValue.FromObj(list);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteNewDict(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte count = Instruction.GetB(inst);
        int @base = frame.BaseSlot;
        var dict = new StashDictionary();
        for (int i = 0; i < count; i++)
        {
            object? key = _stack[@base + a + 1 + i * 2].ToObject();
            StashValue valSv = _stack[@base + a + 2 + i * 2];
            object? valObj = valSv.ToObject();

            if (key == null)
            {
                // Spread entry: compiler emits LoadNull for the key slot.
                if (valObj is SpreadMarker sm)
                {
                    if (sm.Items is StashDictionary spreadDict)
                    {
                        foreach (KeyValuePair<object, StashValue> kv in spreadDict.RawEntries())
                            dict.Set(kv.Key, kv.Value);
                    }
                    else if (sm.Items is StashInstance inst2)
                    {
                        foreach (KeyValuePair<string, StashValue> kv in inst2.GetFields())
                            dict.Set(kv.Key, kv.Value);
                    }
                    else
                    {
                        throw new RuntimeError(
                            "Cannot spread non-dict value into dict literal.", GetCurrentSpan(ref frame));
                    }
                }
                // else: null key with non-spread value — skip
            }
            else
            {
                dict.Set(key, valSv);
            }
        }
        _stack[@base + a] = StashValue.FromObj(dict);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteNewRange(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue startVal = _stack[@base + b];
        StashValue endVal = _stack[@base + c];
        StashValue stepVal = _stack[@base + a + 1]; // step pre-loaded by compiler at R(A+1)

        long start = startVal.IsInt ? startVal.AsInt
            : throw new RuntimeError("Range start must be an integer.", GetCurrentSpan(ref frame));
        long end = endVal.IsInt ? endVal.AsInt
            : throw new RuntimeError("Range end must be an integer.", GetCurrentSpan(ref frame));

        long step;
        if (stepVal.IsNull)
            step = start <= end ? 1L : -1L;
        else if (stepVal.IsInt)
            step = stepVal.AsInt;
        else
            throw new RuntimeError("Range step must be an integer.", GetCurrentSpan(ref frame));

        if (step == 0)
            throw new RuntimeError("'range' step cannot be zero.", GetCurrentSpan(ref frame));

        _stack[@base + a] = StashValue.FromObj(new StashRange(start, end, step));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteSpread(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        int @base = frame.BaseSlot;
        object? iterable = _stack[@base + b].ToObject();
        _stack[@base + a] = StashValue.FromObj(new SpreadMarker(iterable!));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteDestructure(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        ushort metaIdx = Instruction.GetBx(inst);
        int @base = frame.BaseSlot;
        var meta = (DestructureMetadata)frame.Chunk.Constants[metaIdx].AsObj!;
        object? initializer = _stack[@base + a].ToObject();
        int writeReg = a; // values written to R(A), R(A+1), ...

        if (meta.Kind == "array")
        {
            if (initializer is not List<StashValue> list)
                throw new RuntimeError(
                    "Array destructuring requires an array value.", GetCurrentSpan(ref frame));

            for (int i = 0; i < meta.Names.Length; i++)
                _stack[@base + writeReg + i] = i < list.Count ? list[i] : StashValue.Null;

            if (meta.RestName != null)
            {
                var rest = new List<StashValue>();
                for (int i = meta.Names.Length; i < list.Count; i++)
                    rest.Add(list[i]);
                _stack[@base + writeReg + meta.Names.Length] = StashValue.FromObj(rest);
            }
        }
        else
        {
            if (initializer is StashInstance si)
            {
                var usedNames = new HashSet<string>(meta.Names);
                for (int i = 0; i < meta.Names.Length; i++)
                    _stack[@base + writeReg + i] = si.GetField(meta.Names[i], null);

                if (meta.RestName != null)
                {
                    var rest = new StashDictionary();
                    foreach (KeyValuePair<string, StashValue> kv in si.GetAllFields())
                        if (!usedNames.Contains(kv.Key)) rest.Set(kv.Key, kv.Value);
                    _stack[@base + writeReg + meta.Names.Length] = StashValue.FromObj(rest);
                }
            }
            else if (initializer is StashDictionary dict)
            {
                var usedNames = new HashSet<string>(meta.Names);
                for (int i = 0; i < meta.Names.Length; i++)
                    _stack[@base + writeReg + i] = dict.Has(meta.Names[i])
                        ? dict.Get(meta.Names[i])
                        : StashValue.Null;

                if (meta.RestName != null)
                {
                    var rest = new StashDictionary();
                    foreach (StashValue k in dict.Keys())
                        if (k.ToObject() is string ks && !usedNames.Contains(ks))
                            rest.Set(ks, dict.Get(ks));
                    _stack[@base + writeReg + meta.Names.Length] = StashValue.FromObj(rest);
                }
            }
            else
            {
                throw new RuntimeError(
                    "Object destructuring requires a struct instance or dictionary.",
                    GetCurrentSpan(ref frame));
            }
        }
    }
}
