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

    private static StashIterator CreateIterator(object? iterable, SourceSpan? span)
    {
        if (iterable is StashDictionary dict)
        {
            IEnumerator<StashValue> keyEnum = dict.IterableKeys()
                .Select(k => StashValue.FromObject(k))
                .GetEnumerator();
            return new StashIterator(keyEnum, dict);
        }

        if (iterable is List<StashValue> svList)
        {
            return new StashIterator(new List<StashValue>(svList).GetEnumerator());
        }

        if (iterable is StashRange range)
        {
            return new StashIterator(range.IterateValues().GetEnumerator());
        }

        if (iterable is string s)
        {
            return new StashIterator(RuntimeValues.StringToStashValues(s).GetEnumerator());
        }

        throw new RuntimeError($"Cannot iterate over {RuntimeValues.Stringify(iterable)}.", span);
    }

    private void ExecuteArray(ref CallFrame frame)
    {
        ushort count = ReadU16(ref frame);
        var list = new List<StashValue>(count);
        int start = _sp - count;
        for (int i = start; i < _sp; i++)
        {
            StashValue val = _stack[i];
            if (val.IsObj && val.AsObj is SpreadMarker sm)
            {
                if (sm.Items is List<StashValue> spreadSvItems)
                {
                    list.AddRange(spreadSvItems);
                }
                else
                {
                    throw new RuntimeError("Spread operator requires an array.",
                        GetCurrentSpan(ref frame));
                }
            }
            else
            {
                list.Add(val);
            }
        }
        _sp = start;
        Push(StashValue.FromObj(list));
    }

    private void ExecuteDict(ref CallFrame frame)
    {
        ushort count = ReadU16(ref frame);
        var dict = new StashDictionary();
        int start = _sp - (count * 2);
        for (int i = start; i < _sp; i += 2)
        {
            object? key = _stack[i].ToObject();
            StashValue valSv = _stack[i + 1];
            object? valObj = valSv.ToObject();
            if (valObj is SpreadMarker sm)
            {
                if (sm.Items is StashDictionary spreadDict)
                {
                    foreach (KeyValuePair<object, StashValue> kv in spreadDict.RawEntries())
                    {
                        dict.Set(kv.Key, kv.Value);
                    }
                }
                else if (sm.Items is StashInstance inst)
                {
                    foreach (KeyValuePair<string, StashValue> kv in inst.GetFields())
                    {
                        dict.Set(kv.Key, kv.Value);
                    }
                }
                else
                {
                    throw new RuntimeError("Cannot spread non-dict value into dict literal.",
                        GetCurrentSpan(ref frame));
                }
            }
            else
            {
                dict.Set(key!, valSv);
            }
        }
        _sp = start;
        Push(StashValue.FromObj(dict));
    }

    private void ExecuteRange(ref CallFrame frame)
    {
        StashValue step = Pop();
        StashValue end = Pop();
        StashValue start = Pop();
        long s = start.IsInt ? start.AsInt
            : throw new RuntimeError("Range start must be an integer.", GetCurrentSpan(ref frame));
        long e = end.IsInt ? end.AsInt
            : throw new RuntimeError("Range end must be an integer.", GetCurrentSpan(ref frame));
        long st = step.IsInt ? step.AsInt : (s <= e ? 1L : -1L);
        if (st == 0)
        {
            throw new RuntimeError("'range' step cannot be zero.", GetCurrentSpan(ref frame));
        }

        Push(StashValue.FromObj(new StashRange(s, e, st)));
    }

    private void ExecuteSpread(ref CallFrame frame)
    {
        object? iterable = Pop().ToObject();
        Push(StashValue.FromObj(new SpreadMarker(iterable!)));
    }

    private void ExecuteStructInit(ref CallFrame frame)
    {
        ushort fieldCount = ReadU16(ref frame);
        SourceSpan? span = GetCurrentSpan(ref frame);
        // Stack layout: [structDef][name0][val0][name1][val1]...
        var providedFields = new Dictionary<string, StashValue>(fieldCount);
        int pairStart = _sp - (fieldCount * 2);
        for (int i = pairStart; i < _sp; i += 2)
        {
            string fname = (string)_stack[i].AsObj!;
            if (providedFields.ContainsKey(fname))
            {
                throw new RuntimeError($"Duplicate field '{fname}' in struct initialization.", span);
            }

            providedFields[fname] = _stack[i + 1];
        }
        _sp = pairStart;
        object? structDef = Pop().ToObject();
        if (structDef is StashStruct ss)
        {
            // Initialize all declared fields to null, then override with provided values
            var allFields = new Dictionary<string, StashValue>(ss.Fields.Count);
            foreach (string f in ss.Fields)
            {
                allFields[f] = StashValue.Null;
            }

            foreach (KeyValuePair<string, StashValue> kvp in providedFields)
            {
                if (!allFields.ContainsKey(kvp.Key))
                {
                    throw new RuntimeError($"Unknown field '{kvp.Key}' for struct '{ss.Name}'.", span);
                }

                allFields[kvp.Key] = kvp.Value;
            }

            Push(StashValue.FromObj(new StashInstance(ss.Name, ss, allFields)));
        }
        else
        {
            throw new RuntimeError("Not a struct type.", span);
        }
    }

    private void ExecuteDestructure(ref CallFrame frame)
    {
        ushort metaDestructIdx = ReadU16(ref frame);
        var destructMeta = (DestructureMetadata)frame.Chunk.Constants[metaDestructIdx].AsObj!;

        object? initializer = Pop().ToObject();

        if (destructMeta.Kind == "array")
        {
            if (initializer is not List<StashValue> svList)
            {
                throw new RuntimeError("Array destructuring requires an array value.", GetCurrentSpan(ref frame));
            }

            for (int i = 0; i < destructMeta.Names.Length; i++)
            {
                Push(i < svList.Count ? svList[i] : StashValue.Null);
            }

            if (destructMeta.RestName != null)
            {
                var rest = new List<StashValue>();
                for (int i = destructMeta.Names.Length; i < svList.Count; i++)
                {
                    rest.Add(svList[i]);
                }
                Push(StashValue.FromObj(rest));
            }
        }
        else
        {
            if (initializer is StashInstance destructInst)
            {
                var usedNames = new HashSet<string>(destructMeta.Names);
                foreach (string dname in destructMeta.Names)
                {
                    Push(destructInst.GetField(dname, null));
                }

                if (destructMeta.RestName != null)
                {
                    var rest = new StashDictionary();
                    foreach (KeyValuePair<string, StashValue> kvp in destructInst.GetAllFields())
                    {
                        if (!usedNames.Contains(kvp.Key))
                        {
                            rest.Set(kvp.Key, kvp.Value);
                        }
                    }
                    Push(StashValue.FromObj(rest));
                }
            }
            else if (initializer is StashDictionary destructDict)
            {
                var usedNames = new HashSet<string>(destructMeta.Names);
                foreach (string dname in destructMeta.Names)
                {
                    Push(destructDict.Has(dname) ? destructDict.Get(dname) : StashValue.Null);
                }

                if (destructMeta.RestName != null)
                {
                    var rest = new StashDictionary();
                    var allKeys = destructDict.Keys();
                    foreach (StashValue k in allKeys)
                    {
                        if (k.ToObject() is string ks && !usedNames.Contains(ks))
                        {
                            rest.Set(ks, destructDict.Get(ks));
                        }
                    }
                    Push(StashValue.FromObj(rest));
                }
            }
            else
            {
                throw new RuntimeError(
                    "Object destructuring requires a struct instance or dictionary.", GetCurrentSpan(ref frame));
            }
        }
    }

    private void ExecuteIterator(ref CallFrame frame)
    {
        SourceSpan? span = GetCurrentSpan(ref frame);
        object? iterable = Pop().ToObject();
        Push(StashValue.FromObj(CreateIterator(iterable, span)));
    }

    private void ExecuteIterate(ref CallFrame frame)
    {
        short exitOffset = ReadI16(ref frame);

        // Find the StashIterator in the current for-in scope by scanning backward
        // (at most 3 slots: iterator, optional index var, loop var)
        StashIterator? iter = null;
        int iterSlot = -1;
        for (int i = _sp - 1; i >= Math.Max(frame.BaseSlot, _sp - 4); i--)
        {
            if (_stack[i].AsObj is StashIterator found)
            {
                iter = found;
                iterSlot = i;
                break;
            }
        }
        if (iter == null)
        {
            throw new RuntimeError("Internal error: no active iterator.", GetCurrentSpan(ref frame));
        }

        if (!iter.MoveNext())
        {
            frame.IP += exitOffset;
        }
        else
        {
            Push(iter.Current);
            // Update index variable if present.
            // Layout: [iter @ iterSlot][indexVar @ iterSlot+1][loopVar] = 3 locals
            //         [iter @ iterSlot][loopVar]                         = 2 locals
            // After Push, _sp increased by 1; forInLocals = (_sp - 1) - iterSlot
            int forInLocals = (_sp - 1) - iterSlot;
            if (forInLocals == 3)
            {
                if (iter.Dictionary != null)
                {
                    // Dict key-value iteration: Current is the key as StashValue
                    StashValue key = iter.Current;
                    _stack[iterSlot + 1] = key;  // key
                    _stack[_sp - 1] = iter.Dictionary.Get(key.ToObject()!);  // value
                }
                else
                {
                    _stack[iterSlot + 1] = StashValue.FromInt(iter.Index);
                }
            }
        }
    }

    private void ExecuteGetField(ref CallFrame frame)
    {
        ushort nameIdx = ReadU16(ref frame);
        string fieldName = (string)frame.Chunk.Constants[nameIdx].AsObj!;
        StashValue objVal = Pop();

        // Fast path: namespace member access (e.g. math.abs) — no span needed
        if (objVal.Tag == StashValueTag.Obj)
        {
            object? rawObj = objVal.AsObj;
            if (rawObj is StashNamespace ns)
            {
                Push(ns.GetMemberValue(fieldName, null));
                return;
            }
        }

        // General path
        object? obj = objVal.ToObject();
        Push(StashValue.FromObject(GetFieldValue(obj, fieldName, GetCurrentSpan(ref frame))));
    }

    private void ExecuteSetField(ref CallFrame frame)
    {
        ushort nameIdx = ReadU16(ref frame);
        string fieldName = (string)frame.Chunk.Constants[nameIdx].AsObj!;
        StashValue valueVal = Pop();
        object? value = valueVal.ToObject();
        object? obj = Pop().ToObject();
        SetFieldValue(obj, fieldName, value, GetCurrentSpan(ref frame));
        Push(valueVal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteGetIndex(ref CallFrame frame)
    {
        StashValue indexVal = Pop();
        StashValue objVal = Pop();

        // Fast path: array[int] — StashValue list (no boxing)
        if (objVal.Tag == StashValueTag.Obj && objVal.AsObj is List<StashValue> svList && indexVal.IsInt)
        {
            long i = indexVal.AsInt;
            if (i < 0) i += svList.Count;
            if ((ulong)i < (ulong)svList.Count)
            {
                Push(svList[(int)i]);
                return;
            }
            throw new RuntimeError($"Index {indexVal.AsInt} out of bounds for array of length {svList.Count}.",
                GetCurrentSpan(ref frame));
        }

        // General path — falls through to existing logic with lazy span
        Push(StashValue.FromObject(GetIndexValue(objVal.ToObject(), indexVal.ToObject(), ref frame)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSetIndex(ref CallFrame frame)
    {
        StashValue valueVal = Pop();
        StashValue indexVal = Pop();
        StashValue objVal = Pop();

        // Fast path: array[int] = value — StashValue list (no boxing)
        if (objVal.Tag == StashValueTag.Obj && objVal.AsObj is List<StashValue> svList && indexVal.IsInt)
        {
            long i = indexVal.AsInt;
            if (i < 0) i += svList.Count;
            if ((ulong)i < (ulong)svList.Count)
            {
                svList[(int)i] = valueVal;
                Push(valueVal);
                return;
            }
            throw new RuntimeError($"Index {indexVal.AsInt} out of bounds for array of length {svList.Count}.",
                GetCurrentSpan(ref frame));
        }

        // General path with lazy span
        SetIndexValue(objVal.ToObject(), indexVal.ToObject(), valueVal.ToObject(), ref frame);
        Push(valueVal);
    }
}
