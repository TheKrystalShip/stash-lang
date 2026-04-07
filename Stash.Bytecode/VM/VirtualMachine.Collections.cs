using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Array, dictionary, struct, destructure, and iterator opcode handlers.
/// </summary>
public sealed partial class VirtualMachine
{
    private static object? GetIndexValue(object? obj, object? index, SourceSpan? span)
    {
        if (obj is List<object?> list)
        {
            if (index is not long i)
            {
                throw new RuntimeError("Array index must be an integer.", span);
            }

            if (i < 0)
            {
                i += list.Count;
            }

            if (i < 0 || i >= list.Count)
            {
                throw new RuntimeError($"Index {index} out of bounds for array of length {list.Count}.", span);
            }

            return list[(int)i];
        }
        if (obj is StashDictionary dict)
        {
            if (index is null)
            {
                throw new RuntimeError("Dictionary key cannot be null.", span);
            }

            return dict.Get(index);
        }

        if (obj is string s)
        {
            if (index is not long idx)
            {
                throw new RuntimeError("String index must be an integer.", span);
            }

            if (idx < 0)
            {
                idx += s.Length;
            }

            if (idx < 0 || idx >= s.Length)
            {
                throw new RuntimeError($"Index {index} out of bounds for string of length {s.Length}.", span);
            }

            return s[(int)idx].ToString();
        }
        throw new RuntimeError($"Cannot index into {RuntimeValues.Stringify(obj)}.", span);
    }

    private static void SetIndexValue(object? obj, object? index, object? value, SourceSpan? span)
    {
        if (obj is List<object?> list)
        {
            if (index is not long i)
            {
                throw new RuntimeError("Array index must be an integer.", span);
            }

            if (i < 0)
            {
                i += list.Count;
            }

            if (i < 0 || i >= list.Count)
            {
                throw new RuntimeError($"Index {index} out of bounds for array of length {list.Count}.", span);
            }

            list[(int)i] = value;
            return;
        }
        if (obj is StashDictionary dict)
        {
            if (index is null)
            {
                throw new RuntimeError("Dictionary key cannot be null.", span);
            }

            dict.Set(index, value);
            return;
        }
        throw new RuntimeError($"Cannot index-assign into {RuntimeValues.Stringify(obj)}.", span);
    }

    private static StashIterator CreateIterator(object? iterable, SourceSpan? span)
    {
        if (iterable is StashDictionary dict)
        {
            return new StashIterator(dict.Keys().GetEnumerator(), dict);
        }

        IEnumerator<object?> enumerator = iterable switch
        {
            List<object?> list    => new List<object?>(list).GetEnumerator(),
            StashRange range      => range.Iterate().GetEnumerator(),
            string s              => RuntimeValues.StringToChars(s).GetEnumerator(),
            _ => throw new RuntimeError($"Cannot iterate over {RuntimeValues.Stringify(iterable)}.", span),
        };
        return new StashIterator(enumerator);
    }

    private void ExecuteArray(ref CallFrame frame)
    {
        ushort count = ReadU16(ref frame);
        var list = new List<object?>(count);
        int start = _sp - count;
        for (int i = start; i < _sp; i++)
        {
            object? val = _stack[i].ToObject();
            if (val is SpreadMarker sm)
            {
                if (sm.Items is List<object?> spreadItems)
                {
                    list.AddRange(spreadItems);
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
            object? val = _stack[i + 1].ToObject();
            if (val is SpreadMarker sm)
            {
                if (sm.Items is StashDictionary spreadDict)
                {
                    foreach (KeyValuePair<object, object?> kv in spreadDict.RawEntries())
                    {
                        dict.Set(kv.Key, kv.Value);
                    }
                }
                else if (sm.Items is StashInstance inst)
                {
                    foreach (KeyValuePair<string, object?> kv in inst.GetFields())
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
                dict.Set(key!, val);
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
        var providedFields = new Dictionary<string, object?>(fieldCount);
        int pairStart = _sp - (fieldCount * 2);
        for (int i = pairStart; i < _sp; i += 2)
        {
            string fname = (string)_stack[i].AsObj!;
            if (providedFields.ContainsKey(fname))
            {
                throw new RuntimeError($"Duplicate field '{fname}' in struct initialization.", span);
            }

            providedFields[fname] = _stack[i + 1].ToObject();
        }
        _sp = pairStart;
        object? structDef = Pop().ToObject();
        if (structDef is StashStruct ss)
        {
            // Initialize all declared fields to null, then override with provided values
            var allFields = new Dictionary<string, object?>(ss.Fields.Count);
            foreach (string f in ss.Fields)
            {
                allFields[f] = null;
            }

            foreach (KeyValuePair<string, object?> kvp in providedFields)
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
        SourceSpan? span = GetCurrentSpan(ref frame);
        var destructMeta = (DestructureMetadata)frame.Chunk.Constants[metaDestructIdx].AsObj!;

        object? initializer = Pop().ToObject();

        if (destructMeta.Kind == "array")
        {
            if (initializer is not List<object?> list)
            {
                throw new RuntimeError("Array destructuring requires an array value.", span);
            }

            for (int i = 0; i < destructMeta.Names.Length; i++)
            {
                Push(StashValue.FromObject(i < list.Count ? list[i] : null));
            }

            if (destructMeta.RestName != null)
            {
                var rest = new List<object?>();
                for (int i = destructMeta.Names.Length; i < list.Count; i++)
                {
                    rest.Add(list[i]);
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
                    Push(StashValue.FromObject(destructInst.GetField(dname, span)));
                }

                if (destructMeta.RestName != null)
                {
                    var rest = new StashDictionary();
                    foreach (KeyValuePair<string, object?> kvp in destructInst.GetAllFields())
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
                    Push(StashValue.FromObject(destructDict.Has(dname) ? destructDict.Get(dname) : null));
                }

                if (destructMeta.RestName != null)
                {
                    var rest = new StashDictionary();
                    var allKeys = destructDict.Keys();
                    foreach (object? k in allKeys)
                    {
                        if (k is string ks && !usedNames.Contains(ks))
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
                    "Object destructuring requires a struct instance or dictionary.", span);
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
        SourceSpan? span = GetCurrentSpan(ref frame);

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
            throw new RuntimeError("Internal error: no active iterator.", span);
        }

        if (!iter.MoveNext())
        {
            frame.IP += exitOffset;
        }
        else
        {
            Push(StashValue.FromObject(iter.Current));
            // Update index variable if present.
            // Layout: [iter @ iterSlot][indexVar @ iterSlot+1][loopVar] = 3 locals
            //         [iter @ iterSlot][loopVar]                         = 2 locals
            // After Push, _sp increased by 1; forInLocals = (_sp - 1) - iterSlot
            int forInLocals = (_sp - 1) - iterSlot;
            if (forInLocals == 3)
            {
                if (iter.Dictionary != null)
                {
                    // Dict key-value iteration: Current = key, look up value
                    object? dictKey = iter.Current;
                    _stack[iterSlot + 1] = StashValue.FromObject(dictKey);  // key
                    _stack[_sp - 1] = StashValue.FromObject(iter.Dictionary.Get(dictKey!));  // value
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
        object? obj = Pop().ToObject();
        Push(StashValue.FromObject(GetFieldValue(obj, fieldName, GetCurrentSpan(ref frame))));
    }

    private void ExecuteSetField(ref CallFrame frame)
    {
        ushort nameIdx = ReadU16(ref frame);
        string fieldName = (string)frame.Chunk.Constants[nameIdx].AsObj!;
        object? value = Pop().ToObject();
        object? obj = Pop().ToObject();
        SetFieldValue(obj, fieldName, value, GetCurrentSpan(ref frame));
        Push(StashValue.FromObject(value));
    }

    private void ExecuteGetIndex(ref CallFrame frame)
    {
        object? index = Pop().ToObject();
        object? obj = Pop().ToObject();
        Push(StashValue.FromObject(GetIndexValue(obj, index, GetCurrentSpan(ref frame))));
    }

    private void ExecuteSetIndex(ref CallFrame frame)
    {
        object? value = Pop().ToObject();
        object? index = Pop().ToObject();
        object? obj = Pop().ToObject();
        SetIndexValue(obj, index, value, GetCurrentSpan(ref frame));
        Push(StashValue.FromObject(value));
    }
}
