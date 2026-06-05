namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the <c>arr</c> namespace built-in functions.
/// </summary>
[StashNamespace]
public static partial class ArrBuiltIns
{
    /// <summary>Appends a value to the end of the array. Mutates the original array.</summary>
    /// <param name="array">The array to modify</param>
    /// <param name="value">The value to append</param>
    /// <exception cref="TypeError">if `array` is not an array</exception>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void Push(IInterpreterContext ctx, StashValue array, StashValue value)
    {
        if (array.IsObj && IsArrayFrozen(array.AsObj))
            throw new ReadOnlyError("Cannot mutate a frozen array.");
        if (array.IsObj && array.AsObj is StashTypedArray taPush)
        {
            taPush.Add(value);
            return;
        }
        if (array.IsObj && array.AsObj is List<StashValue> list)
        {
            list.Add(value);
            return;
        }
        throw new TypeError("First argument to 'arr.push' must be an array.");
    }

    /// <summary>Removes and returns the last element of the array. Throws if empty.</summary>
    /// <param name="array">The array to pop from</param>
    /// <exception cref="ValueError">if the array is empty</exception>
    /// <exception cref="TypeError">if `array` is not an array</exception>
    /// <returns>The removed last element</returns>
    [StashFn(ReturnType = "any")]
    private static StashValue Pop(IInterpreterContext ctx, StashValue array)
    {
        if (array.IsObj && IsArrayFrozen(array.AsObj))
            throw new ReadOnlyError("Cannot mutate a frozen array.");
        if (array.IsObj && array.AsObj is StashTypedArray taPop)
        {
            if (taPop.Count == 0)
                throw new ValueError("Cannot pop from an empty array.");
            return taPop.RemoveLast();
        }
        if (array.IsObj && array.AsObj is List<StashValue> list)
        {
            if (list.Count == 0)
                throw new ValueError("Cannot pop from an empty array.");
            var last = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return last;
        }
        throw new TypeError("First argument to 'arr.pop' must be an array.");
    }

    /// <summary>Returns the last element without removing it. Throws if empty.</summary>
    /// <param name="array">The array to peek</param>
    /// <exception cref="ValueError">if the array is empty</exception>
    /// <returns>The last element</returns>
    [StashFn(ReturnType = "any")]
    private static StashValue Peek(IInterpreterContext ctx, List<StashValue> array)
    {
        if (array.Count == 0)
            throw new ValueError("Cannot peek an empty array.");
        return array[array.Count - 1];
    }

    /// <summary>Inserts a value at the specified index. Supports negative indexing for typed arrays.</summary>
    /// <param name="array">The array to modify</param>
    /// <param name="index">The position to insert at</param>
    /// <param name="value">The value to insert</param>
    /// <exception cref="IndexError">if `index` is out of bounds</exception>
    /// <exception cref="TypeError">if `array` is not an array</exception>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void Insert(IInterpreterContext ctx, StashValue array, long index, StashValue value)
    {
        if (array.IsObj && IsArrayFrozen(array.AsObj))
            throw new ReadOnlyError("Cannot mutate a frozen array.");
        if (array.IsObj && array.AsObj is StashTypedArray taInsert)
        {
            int i = (int)(index < 0 ? index + taInsert.Count : index);
            if (i < 0 || i > taInsert.Count)
                throw new IndexError($"Index {index} is out of bounds for 'arr.insert'.");
            taInsert.Insert(i, value);
            return;
        }
        if (array.IsObj && array.AsObj is List<StashValue> list)
        {
            if (index < 0 || index > list.Count)
                throw new IndexError($"Index {index} is out of bounds for 'arr.insert'.");
            list.Insert((int)index, value);
            return;
        }
        throw new TypeError("First argument to 'arr.insert' must be an array.");
    }

    /// <summary>Removes and returns the element at the specified index.</summary>
    /// <param name="array">The array to modify</param>
    /// <param name="index">The index of the element to remove</param>
    /// <exception cref="IndexError">if `index` is out of bounds</exception>
    /// <exception cref="TypeError">if `array` is not an array</exception>
    /// <returns>The removed element</returns>
    [StashFn(ReturnType = "any")]
    private static StashValue RemoveAt(IInterpreterContext ctx, StashValue array, long index)
    {
        if (array.IsObj && IsArrayFrozen(array.AsObj))
            throw new ReadOnlyError("Cannot mutate a frozen array.");
        if (array.IsObj && array.AsObj is StashTypedArray taRemoveAt)
        {
            int i = (int)(index < 0 ? index + taRemoveAt.Count : index);
            taRemoveAt.CheckBounds(i, "removeAt");
            StashValue removed = taRemoveAt.Get(i);
            taRemoveAt.RemoveAt(i);
            return removed;
        }
        if (array.IsObj && array.AsObj is List<StashValue> list)
        {
            if (index < 0 || index >= list.Count)
                throw new IndexError($"Index {index} is out of bounds for 'arr.removeAt'.");
            var removedVal = list[(int)index];
            list.RemoveAt((int)index);
            return removedVal;
        }
        throw new TypeError("First argument to 'arr.removeAt' must be an array.");
    }

    /// <summary>Removes the first occurrence of a value from the array. Returns true if found and removed.</summary>
    /// <param name="array">The array to modify</param>
    /// <param name="value">The value to remove</param>
    /// <exception cref="TypeError">if `array` is not an array</exception>
    /// <returns>true if the value was found and removed, false otherwise</returns>
    [StashFn(ReturnType = "bool")]
    private static bool Remove(IInterpreterContext ctx, StashValue array, StashValue value)
    {
        if (array.IsObj && IsArrayFrozen(array.AsObj))
            throw new ReadOnlyError("Cannot mutate a frozen array.");
        if (array.IsObj && array.AsObj is StashTypedArray taRemove)
        {
            object? target = value.ToObject();
            for (int i = 0; i < taRemove.Count; i++)
            {
                if (RuntimeValues.IsEqual(taRemove.Get(i).ToObject(), target))
                {
                    taRemove.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
        if (array.IsObj && array.AsObj is List<StashValue> list)
        {
            var target2 = value.ToObject();
            for (int i = 0; i < list.Count; i++)
            {
                if (RuntimeValues.IsEqual(list[i].ToObject(), target2))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
        throw new TypeError("First argument to 'arr.remove' must be an array.");
    }

    /// <summary>Removes all elements from the array. Mutates the original array.</summary>
    /// <param name="array">The array to clear</param>
    /// <exception cref="TypeError">if `array` is not an array</exception>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void Clear(IInterpreterContext ctx, StashValue array)
    {
        if (array.IsObj && IsArrayFrozen(array.AsObj))
            throw new ReadOnlyError("Cannot mutate a frozen array.");
        if (array.IsObj && array.AsObj is StashTypedArray taClear)
        {
            taClear.Clear();
            return;
        }
        if (array.IsObj && array.AsObj is List<StashValue> list)
        {
            list.Clear();
            return;
        }
        throw new TypeError("First argument to 'arr.clear' must be an array.");
    }

    /// <summary>Returns true if the array contains the specified value.</summary>
    /// <param name="array">The array to search</param>
    /// <param name="value">The value to look for</param>
    /// <returns>true if the value exists in the array, false otherwise</returns>
    [StashFn(ReturnType = "bool")]
    private static bool Contains(IInterpreterContext ctx, List<StashValue> array, StashValue value)
    {
        var target = value.ToObject();
        foreach (var item in array)
        {
            if (RuntimeValues.IsEqual(item.ToObject(), target))
                return true;
        }
        return false;
    }

    /// <summary>Returns true if the array contains the given value. Optional startIndex (default 0) sets the starting search position.</summary>
    /// <param name="array">The array to search</param>
    /// <param name="value">The value to look for</param>
    /// <param name="startIndex">The index to start searching from (default 0; negative counts from end)</param>
    /// <returns>true if the value is found at or after startIndex, false otherwise</returns>
    [StashFn(ReturnType = "bool")]
    private static bool Includes(IInterpreterContext ctx, List<StashValue> array, StashValue value, long startIndex = 0)
    {
        int start = (int)startIndex;
        if (start < 0) start = Math.Max(0, array.Count + start);
        var target = value.ToObject();
        for (int i = start; i < array.Count; i++)
        {
            if (RuntimeValues.IsEqual(array[i].ToObject(), target))
                return true;
        }
        return false;
    }

    /// <summary>Returns the index of the first occurrence of value in the array. Optional startIndex (default 0) sets the starting search position. Returns -1 if not found.</summary>
    /// <param name="array">The array to search</param>
    /// <param name="value">The value to look for</param>
    /// <param name="startIndex">The index to start searching from (default 0; negative counts from end)</param>
    /// <returns>The index of the first match at or after startIndex, or -1 if not found</returns>
    [StashFn(ReturnType = "int")]
    private static long IndexOf(IInterpreterContext ctx, List<StashValue> array, StashValue value, long startIndex = 0)
    {
        int start = (int)startIndex;
        if (start < 0) start = Math.Max(0, array.Count + start);
        var target = value.ToObject();
        for (int i = start; i < array.Count; i++)
        {
            if (RuntimeValues.IsEqual(array[i].ToObject(), target))
                return (long)i;
        }
        return -1L;
    }

    /// <summary>Returns the index of the last occurrence of value in the array, searching backwards from the end (or from startIndex if provided). Returns -1 if not found.</summary>
    /// <param name="array">The array to search</param>
    /// <param name="value">The value to look for</param>
    /// <param name="startIndex">The index to start searching backwards from (default is last element; negative counts from end)</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>The index of the last match, or -1 if not found</returns>
    // Raw = true: the default for startIndex depends on the array length (list.Count - 1),
    // which cannot be expressed as a C# constant default value.
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue LastIndexOf(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 2 || args.Length > 3)
            throw new RuntimeError("'arr.lastIndexOf' requires 2 or 3 arguments.");
        var list = SvArgs.StashList(args, 0, "arr.lastIndexOf");
        var value = args[1].ToObject();
        int start = args.Length == 3 ? (int)SvArgs.Long(args, 2, "arr.lastIndexOf") : list.Count - 1;
        if (start < 0) start = list.Count + start;
        start = Math.Min(start, list.Count - 1);
        for (int i = start; i >= 0; i--)
        {
            if (RuntimeValues.IsEqual(list[i].ToObject(), value))
                return StashValue.FromInt((long)i);
        }
        return StashValue.FromInt(-1L);
    }

    /// <summary>Returns a new array containing elements from start index to end index (exclusive).</summary>
    /// <param name="array">The source array</param>
    /// <param name="start">The start index (inclusive)</param>
    /// <param name="end">The end index (exclusive)</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>A new array with the specified range of elements</returns>
    // Raw = true: inspects args[0] for StashTypedArray to preserve element type in the
    // returned array. The typed form would receive a materialized List<StashValue> and
    // the element type information would be lost.
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Slice(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string? elementType = args[0].IsObj && args[0].AsObj is StashTypedArray srcSl ? srcSl.ElementTypeName : null;
        var list = SvArgs.StashList(args, 0, "arr.slice");
        var start = SvArgs.Long(args, 1, "arr.slice");
        var end = SvArgs.Long(args, 2, "arr.slice");
        int s = (int)Math.Max(0, Math.Min(start, list.Count));
        int e = (int)Math.Max(0, Math.Min(end, list.Count));
        if (e < s) e = s;
        var result = new StashArray(list.GetRange(s, e - s));
        return elementType != null
            ? StashValue.FromObj(StashTypedArray.Create(elementType, result))
            : StashValue.FromObj(result);
    }

    /// <summary>Returns a new array combining two arrays.</summary>
    /// <param name="a">The first array</param>
    /// <param name="b">The second array</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>A new array containing all elements from a followed by all elements from b</returns>
    // Raw = true: inspects both args[0] and args[1] for StashTypedArray to preserve element
    // type in the returned array when both inputs share the same type.
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Concat(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string? et1 = args[0].IsObj && args[0].AsObj is StashTypedArray srcCa ? srcCa.ElementTypeName : null;
        string? et2 = args[1].IsObj && args[1].AsObj is StashTypedArray srcCb ? srcCb.ElementTypeName : null;
        var list1 = SvArgs.StashList(args, 0, "arr.concat");
        var list2 = SvArgs.StashList(args, 1, "arr.concat");
        var result = new StashArray(list1.Count + list2.Count);
        result.AddRange(list1);
        result.AddRange(list2);
        return et1 != null && et1 == et2
            ? StashValue.FromObj(StashTypedArray.Create(et1, result))
            : StashValue.FromObj(result);
    }

    /// <summary>Joins array elements into a string. Optional separator defaults to ",".</summary>
    /// <param name="array">The array whose elements to join</param>
    /// <param name="separator">The string to place between elements (default ",")</param>
    /// <returns>A string of all elements concatenated with the separator</returns>
    [StashFn(ReturnType = "string")]
    private static string Join(IInterpreterContext ctx, List<StashValue> array, string separator = ",")
    {
        var parts = new string[array.Count];
        for (int i = 0; i < array.Count; i++)
            parts[i] = RuntimeValues.Stringify(array[i].ToObject());
        return string.Join(separator, parts);
    }

    /// <summary>Reverses the array in place. Mutates the original array.</summary>
    /// <param name="array">The array to reverse</param>
    /// <exception cref="TypeError">if `array` is not an array</exception>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void Reverse(IInterpreterContext ctx, StashValue array)
    {
        if (array.IsObj && IsArrayFrozen(array.AsObj))
            throw new ReadOnlyError("Cannot mutate a frozen array.");
        if (array.IsObj && array.AsObj is StashTypedArray taReverse)
        {
            for (int i = 0, j = taReverse.Count - 1; i < j; i++, j--)
            {
                StashValue tmp = taReverse.Get(i);
                taReverse.Set(i, taReverse.Get(j));
                taReverse.Set(j, tmp);
            }
            return;
        }
        if (array.IsObj && array.AsObj is List<StashValue> list)
        {
            list.Reverse();
            return;
        }
        throw new TypeError("First argument to 'arr.reverse' must be an array.");
    }

    /// <summary>Sorts the array in place. When comparator is provided, it receives two elements and must return a negative number (a less than b), zero (a == b), or positive number (a greater than b).</summary>
    /// <param name="array">The array to sort in place</param>
    /// <param name="comparator">A function(a, b) returning negative, zero, or positive to define sort order</param>
    /// <exception cref="TypeError">if `array` is not an array, if the comparator does not return a number, or if elements have incompatible types</exception>
    /// <returns>null (the array is sorted in place)</returns>
    // Raw = true: handles both List<StashValue> and StashTypedArray polymorphically,
    // and has an optional callable parameter. See Push comment above.
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Sort(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new RuntimeError("'arr.sort' requires 1 or 2 arguments.");
        StashValue arrSortVal = args[0];
        if (arrSortVal.IsObj && IsArrayFrozen(arrSortVal.AsObj))
            throw new ReadOnlyError("Cannot mutate a frozen array.");
        if (arrSortVal.IsObj && arrSortVal.AsObj is StashTypedArray taSort)
        {
            var tempList = new List<StashValue>(taSort.Count);
            for (int i = 0; i < taSort.Count; i++) tempList.Add(taSort.Get(i));
            try
            {
                if (args.Length == 2)
                {
                    var cmp = SvArgs.Callable(args, 1, "arr.sort");
                    tempList.Sort((a, b) =>
                    {
                        var res = ctx.InvokeCallbackDirect(cmp, new StashValue[] { a, b }).ToObject();
                        if (res is long l) return l < 0 ? -1 : l > 0 ? 1 : 0;
                        if (res is double d) return d < 0 ? -1 : d > 0 ? 1 : 0;
                        throw new TypeError("'arr.sort' comparator must return a number.");
                    });
                }
                else
                {
                    tempList.Sort((a, b) =>
                    {
                        var ao = a.ToObject();
                        var bo = b.ToObject();
                        if (ao is long la && bo is long lb) return la.CompareTo(lb);
                        if (ao is double da && bo is double db) return da.CompareTo(db);
                        if (ao is long la2 && bo is double db2) return ((double)la2).CompareTo(db2);
                        if (ao is double da2 && bo is long lb2) return da2.CompareTo((double)lb2);
                        if (ao is string sa && bo is string sb) return string.Compare(sa, sb, StringComparison.Ordinal);
                        throw new TypeError("Cannot compare values of incompatible types in 'arr.sort'.");
                    });
                }
            }
            catch (InvalidOperationException ex) when (ex.InnerException is RuntimeError re)
            {
                throw re;
            }
            for (int i = 0; i < tempList.Count; i++) taSort.Set(i, tempList[i]);
            return StashValue.Null;
        }
        var list = SvArgs.StashList(args, 0, "arr.sort");
        try
        {
            if (args.Length == 2)
            {
                var cmp = SvArgs.Callable(args, 1, "arr.sort");
                list.Sort((a, b) =>
                {
                    var res = ctx.InvokeCallbackDirect(cmp, new StashValue[] { a, b }).ToObject();
                    if (res is long l) return l < 0 ? -1 : l > 0 ? 1 : 0;
                    if (res is double d) return d < 0 ? -1 : d > 0 ? 1 : 0;
                    throw new TypeError("'arr.sort' comparator must return a number.");
                });
            }
            else
            {
                list.Sort((a, b) =>
                {
                    var ao = a.ToObject();
                    var bo = b.ToObject();
                    if (ao is long la && bo is long lb) return la.CompareTo(lb);
                    if (ao is double da && bo is double db) return da.CompareTo(db);
                    if (ao is long la2 && bo is double db2) return ((double)la2).CompareTo(db2);
                    if (ao is double da2 && bo is long lb2) return da2.CompareTo((double)lb2);
                    if (ao is string sa && bo is string sb) return string.Compare(sa, sb, StringComparison.Ordinal);
                    throw new TypeError("Cannot compare values of incompatible types in 'arr.sort'.");
                });
            }
        }
        catch (InvalidOperationException ex) when (ex.InnerException is RuntimeError re)
        {
            throw re;
        }
        return StashValue.Null;
    }

    /// <summary>Returns a new array with each element transformed by the given function.</summary>
    /// <param name="array">The source array</param>
    /// <param name="fn">A function that receives each element and returns its transformed value</param>
    /// <returns>A new array of transformed elements</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> Map(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn)
    {
        var result = new StashArray(array.Count);
        foreach (StashValue item in array)
        {
            StashValue mapped = ctx.InvokeCallbackDirect(fn, new StashValue[] { item });
            result.Add(mapped);
        }
        return result;
    }

    /// <summary>Returns a new array containing only elements for which fn returns truthy.</summary>
    /// <param name="array">The source array</param>
    /// <param name="fn">A predicate function that receives each element</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>A new array of elements where fn returned truthy</returns>
    // Raw = true: inspects args[0] for StashTypedArray to preserve element type in the
    // returned array. The typed form would receive a materialized List<StashValue> and
    // the element type information would be lost.
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Filter(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string? elementType = args[0].IsObj && args[0].AsObj is StashTypedArray srcFl ? srcFl.ElementTypeName : null;
        var list = SvArgs.StashList(args, 0, "arr.filter");
        var fn = SvArgs.Callable(args, 1, "arr.filter");
        var result = new StashArray(list.Count);
        foreach (StashValue item in list)
        {
            if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
            {
                result.Add(item);
            }
        }
        return elementType != null
            ? StashValue.FromObj(StashTypedArray.Create(elementType, result))
            : StashValue.FromObj(result);
    }

    /// <summary>Calls fn for each element in the array. Returns null.</summary>
    /// <param name="array">The array to iterate</param>
    /// <param name="fn">A function that receives each element</param>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void ForEach(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn)
    {
        foreach (StashValue item in array)
        {
            ctx.InvokeCallbackDirect(fn, new StashValue[] { item });
        }
    }

    /// <summary>
    /// Like map, but executes the function in parallel across elements using a thread-pool.
    /// Results are returned in the same order as the input array (order-preserving).
    /// If any callback throws, the first error encountered is rethrown (fail-fast).
    /// If the callback is an <c>async</c> function, its returned Future is automatically
    /// awaited, so <c>arr.parMap([1,2,3], async (x) =&gt; x * 2)</c> returns
    /// <c>[2, 4, 6]</c> rather than an array of Futures.
    /// </summary>
    /// <param name="array">The source array</param>
    /// <param name="fn">A function that receives each element and returns its transformed value</param>
    /// <param name="maxConcurrency">Optional maximum number of parallel threads (default: unbounded — uses all available cores). Must be &gt;= 1 if provided.</param>
    /// <exception cref="TypeError">if `array` is not an array or `fn` is not callable</exception>
    /// <exception cref="ValueError">if `maxConcurrency` is less than 1</exception>
    /// <returns>A new array of transformed elements in input order</returns>
    // Raw = true: passes raw args span to ExecuteParMap which also reads optional
    // maxConcurrency from args[2]. The variadic handling doesn't map cleanly to the
    // typed form's params StashValue[] (which would shift the index).
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue ParMap(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var list = new List<StashValue>(args.Length);
        foreach (StashValue sv in args) list.Add(sv);
        return StashValue.FromObject(ExecuteParMap(ctx, list));
    }

    /// <summary>
    /// Like filter, but evaluates the predicate in parallel using a thread-pool.
    /// The relative order of passing elements is preserved (order-preserving).
    /// If any predicate call throws, the first error encountered is rethrown (fail-fast).
    /// If the predicate is an <c>async</c> function, its returned Future is automatically
    /// awaited; truthiness is then tested on the resolved value.
    /// </summary>
    /// <param name="array">The source array</param>
    /// <param name="fn">A predicate function that receives each element; truthy return = keep</param>
    /// <param name="maxConcurrency">Optional maximum number of parallel threads (default: unbounded — uses all available cores). Must be &gt;= 1 if provided.</param>
    /// <exception cref="TypeError">if `array` is not an array or `fn` is not callable</exception>
    /// <exception cref="ValueError">if `maxConcurrency` is less than 1</exception>
    /// <returns>A new array of elements where fn returned truthy, in input order</returns>
    // Raw = true: passes raw args span to ExecuteParFilter which also reads optional
    // maxConcurrency from args[2]. See ParMap comment above.
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue ParFilter(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var list = new List<StashValue>(args.Length);
        foreach (StashValue sv in args) list.Add(sv);
        return StashValue.FromObject(ExecuteParFilter(ctx, list));
    }

    /// <summary>
    /// Like forEach, but executes the function in parallel using a thread-pool.
    /// If any callback throws, the first error encountered is rethrown (fail-fast).
    /// If the callback is an <c>async</c> function, its returned Future is automatically
    /// awaited before the iteration continues for that element.
    /// </summary>
    /// <param name="array">The array to iterate</param>
    /// <param name="fn">A function that receives each element</param>
    /// <param name="maxConcurrency">Optional maximum number of parallel threads (default: unbounded — uses all available cores). Must be &gt;= 1 if provided.</param>
    /// <exception cref="TypeError">if `array` is not an array or `fn` is not callable</exception>
    /// <exception cref="ValueError">if `maxConcurrency` is less than 1</exception>
    /// <returns>null</returns>
    // Raw = true: passes raw args span to ExecuteParForEach which also reads optional
    // maxConcurrency from args[2]. See ParMap comment above.
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue ParForEach(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var list = new List<StashValue>(args.Length);
        foreach (StashValue sv in args) list.Add(sv);
        return StashValue.FromObject(ExecuteParForEach(ctx, list));
    }

    /// <summary>Returns the first element for which fn returns truthy, or null if none found.</summary>
    /// <param name="array">The array to search</param>
    /// <param name="fn">A predicate function that receives each element</param>
    /// <returns>The first matching element, or null</returns>
    [StashFn(ReturnType = "any")]
    private static StashValue Find(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn)
    {
        foreach (StashValue item in array)
        {
            if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
            {
                return item;
            }
        }
        return StashValue.Null;
    }

    /// <summary>Reduces the array to a single value by applying fn(accumulator, element) for each element.</summary>
    /// <param name="array">The array to reduce</param>
    /// <param name="fn">A function that receives the accumulator and current element, and returns the new accumulator</param>
    /// <param name="initial">The initial accumulator value</param>
    /// <returns>The final accumulated value</returns>
    [StashFn(ReturnType = "any")]
    private static StashValue Reduce(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn, StashValue initial)
    {
        StashValue accumulator = initial;
        foreach (StashValue item in array)
        {
            accumulator = ctx.InvokeCallbackDirect(fn, new StashValue[] { accumulator, item });
        }
        return accumulator;
    }

    /// <summary>Returns a new array with duplicate values removed. When fn is provided, uses fn(element) as the uniqueness key.</summary>
    /// <param name="array">The source array</param>
    /// <param name="fn">A function that receives each element and returns the key used for uniqueness comparison</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>A new array with duplicate elements removed</returns>
    // Raw = true: inspects args[0] for StashTypedArray to preserve element type in the
    // returned array, and has an optional callable parameter.
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Unique(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new RuntimeError("'arr.unique' requires 1 or 2 arguments.");
        string? elementType = args[0].IsObj && args[0].AsObj is StashTypedArray srcUniq ? srcUniq.ElementTypeName : null;
        var list = SvArgs.StashList(args, 0, "arr.unique");
        var result = new StashArray();
        if (args.Length == 2)
        {
            var fn = SvArgs.Callable(args, 1, "arr.unique");
            var seenKeys = new List<object?>();
            foreach (var item in list)
            {
                var key = ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject();
                bool found = false;
                foreach (var existingKey in seenKeys)
                {
                    if (RuntimeValues.IsEqual(key, existingKey))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    seenKeys.Add(key);
                    result.Add(item);
                }
            }
        }
        else
        {
            foreach (var item in list)
            {
                bool found = false;
                foreach (var existing in result)
                {
                    if (RuntimeValues.IsEqual(item.ToObject(), existing.ToObject()))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) result.Add(item);
            }
        }
        return elementType != null
            ? StashValue.FromObj(StashTypedArray.Create(elementType, result))
            : StashValue.FromObj(result);
    }

    /// <summary>Returns true if fn returns truthy for at least one element.</summary>
    /// <param name="array">The array to test</param>
    /// <param name="fn">A predicate function that receives each element</param>
    /// <returns>true if any element satisfies the predicate, false otherwise</returns>
    [StashFn(ReturnType = "bool")]
    private static bool Any(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn)
    {
        foreach (StashValue item in array)
        {
            if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Returns true if fn returns truthy for every element.</summary>
    /// <param name="array">The array to test</param>
    /// <param name="fn">A predicate function that receives each element</param>
    /// <returns>true if all elements satisfy the predicate, false otherwise</returns>
    [StashFn(ReturnType = "bool")]
    private static bool Every(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn)
    {
        foreach (StashValue item in array)
        {
            if (!RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Returns a new array with nested arrays flattened. Optional depth (default 1) controls how many levels to flatten; use -1 to flatten completely.</summary>
    /// <param name="array">The source array containing potentially nested arrays</param>
    /// <param name="depth">How many levels of nesting to flatten (default 1; use -1 for full recursion)</param>
    /// <returns>A new array with nested arrays flattened to the specified depth</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> Flat(IInterpreterContext ctx, List<StashValue> array, long depth = 1)
    {
        var result = new StashArray();
        FlattenInto(array, result, (int)depth);
        return result;
    }

    /// <summary>Maps each element with fn, then flattens one level if the result is an array.</summary>
    /// <param name="array">The source array</param>
    /// <param name="fn">A function that receives each element and returns a value or array</param>
    /// <returns>A new flattened array of mapped elements</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> FlatMap(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn)
    {
        var result = new StashArray();
        foreach (StashValue item in array)
        {
            StashValue mapped = ctx.InvokeCallbackDirect(fn, new StashValue[] { item });
            if (mapped.ToObject() is List<StashValue> svInner)
            {
                result.AddRange(svInner);
            }
            else
            {
                result.Add(mapped);
            }
        }
        return result;
    }

    /// <summary>Returns the index of the first element for which fn returns truthy, or -1 if none found.</summary>
    /// <param name="array">The array to search</param>
    /// <param name="fn">A predicate function that receives each element</param>
    /// <returns>The index of the first matching element, or -1</returns>
    [StashFn(ReturnType = "int")]
    private static long FindIndex(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn)
    {
        for (int i = 0; i < array.Count; i++)
        {
            if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { array[i] }).ToObject()))
            {
                return (long)i;
            }
        }
        return -1L;
    }

    /// <summary>Returns the number of elements for which fn returns truthy.</summary>
    /// <param name="array">The array to count</param>
    /// <param name="fn">A predicate function that receives each element</param>
    /// <returns>The count of elements satisfying the predicate</returns>
    [StashFn(ReturnType = "int")]
    private static long Count(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn)
    {
        long count = 0;
        foreach (StashValue item in array)
        {
            if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>Returns a new array sorted by the key returned by fn.</summary>
    /// <param name="array">The source array</param>
    /// <param name="fn">A function that receives each element and returns its sort key</param>
    /// <exception cref="TypeError">if any argument has the wrong type, or if elements have incompatible sort key types</exception>
    /// <returns>A new sorted array</returns>
    // Raw = true: inspects args[0] for StashTypedArray to preserve element type in the
    // returned array. The typed form would receive a materialized List<StashValue> and
    // the element type information would be lost.
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue SortBy(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string? elementType = args[0].IsObj && args[0].AsObj is StashTypedArray srcSby ? srcSby.ElementTypeName : null;
        var list = SvArgs.StashList(args, 0, "arr.sortBy");
        var fn = SvArgs.Callable(args, 1, "arr.sortBy");
        var sorted = new StashArray(list);
        try
        {
            sorted.Sort((a, b) =>
            {
                var ka = ctx.InvokeCallbackDirect(fn, new StashValue[] { a }).ToObject();
                var kb = ctx.InvokeCallbackDirect(fn, new StashValue[] { b }).ToObject();
                return CompareValues(ka, kb);
            });
        }
        catch (InvalidOperationException ex) when (ex.InnerException is RuntimeError re)
        {
            throw re;
        }
        return elementType != null
            ? StashValue.FromObj(StashTypedArray.Create(elementType, sorted))
            : StashValue.FromObj(sorted);
    }

    /// <summary>Groups elements by the key returned by fn. Returns a dict of arrays.</summary>
    /// <param name="array">The source array</param>
    /// <param name="fn">A function that receives each element and returns its group key</param>
    /// <returns>A dictionary mapping keys to arrays of elements with that key</returns>
    [StashFn(ReturnType = "dict")]
    private static StashDictionary GroupBy(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn)
    {
        var result = new StashDictionary();
        foreach (StashValue item in array)
        {
            var key = RuntimeValues.Stringify(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject());
            var existing = result.Get(key);
            if (existing.AsObj is List<StashValue> svGroup)
            {
                svGroup.Add(item);
            }
            else
            {
                result.Set(key, StashValue.FromObj(new StashArray { item }));
            }
        }
        return result;
    }

    /// <summary>Returns the sum of all elements. All elements must be numbers.</summary>
    /// <param name="array">The array of numbers to sum</param>
    /// <exception cref="TypeError">if any element is not a number</exception>
    /// <returns>The sum of all elements</returns>
    [StashFn(ReturnType = "number")]
    private static StashValue Sum(IInterpreterContext ctx, List<StashValue> array)
    {
        long longTotal = 0;
        double doubleTotal = 0;
        bool hasFloat = false;
        foreach (var item in array)
        {
            var v = item.ToObject();
            if (v is long l) longTotal += l;
            else if (v is double d) { doubleTotal += d; hasFloat = true; }
            else throw new TypeError("'arr.sum' requires all elements to be numbers.");
        }
        return hasFloat ? StashValue.FromFloat(doubleTotal + longTotal) : StashValue.FromInt(longTotal);
    }

    /// <summary>Returns the smallest element. All elements must be numbers. Array must not be empty.</summary>
    /// <param name="array">The array of numbers</param>
    /// <exception cref="ValueError">if the array is empty</exception>
    /// <exception cref="TypeError">if any element is not a number</exception>
    /// <returns>The smallest element</returns>
    [StashFn(ReturnType = "number")]
    private static StashValue Min(IInterpreterContext ctx, List<StashValue> array)
    {
        if (array.Count == 0)
            throw new ValueError("'arr.min' requires a non-empty array.");
        double min = double.MaxValue;
        bool hasFloat = false;
        foreach (var item in array)
        {
            var v = item.ToObject();
            if (v is long l) { if (l < min) min = l; }
            else if (v is double d) { if (d < min) min = d; hasFloat = true; }
            else throw new TypeError("'arr.min' requires all elements to be numbers.");
        }
        return hasFloat ? StashValue.FromFloat(min) : StashValue.FromInt((long)min);
    }

    /// <summary>Returns the largest element. All elements must be numbers. Array must not be empty.</summary>
    /// <param name="array">The array of numbers</param>
    /// <exception cref="ValueError">if the array is empty</exception>
    /// <exception cref="TypeError">if any element is not a number</exception>
    /// <returns>The largest element</returns>
    [StashFn(ReturnType = "number")]
    private static StashValue Max(IInterpreterContext ctx, List<StashValue> array)
    {
        if (array.Count == 0)
            throw new ValueError("'arr.max' requires a non-empty array.");
        double max = double.MinValue;
        bool hasFloat = false;
        foreach (var item in array)
        {
            var v = item.ToObject();
            if (v is long l) { if (l > max) max = l; }
            else if (v is double d) { if (d > max) max = d; hasFloat = true; }
            else throw new TypeError("'arr.max' requires all elements to be numbers.");
        }
        return hasFloat ? StashValue.FromFloat(max) : StashValue.FromInt((long)max);
    }

    /// <summary>Pairs elements from two arrays into a new array of [a, b] pairs. Length is the shorter array.</summary>
    /// <param name="a">The first array</param>
    /// <param name="b">The second array</param>
    /// <returns>A new array of two-element pairs</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> Zip(IInterpreterContext ctx, List<StashValue> a, List<StashValue> b)
    {
        int len = Math.Min(a.Count, b.Count);
        var result = new StashArray(len);
        for (int i = 0; i < len; i++)
            result.Add(StashValue.FromObj(new StashArray { a[i], b[i] }));
        return result;
    }

    /// <summary>Splits the array into sub-arrays of the given size.</summary>
    /// <param name="array">The source array</param>
    /// <param name="size">The maximum size of each chunk</param>
    /// <exception cref="ValueError">if `size` is not positive</exception>
    /// <returns>A new array of chunk arrays</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> Chunk(IInterpreterContext ctx, List<StashValue> array, long size)
    {
        if (size <= 0)
            throw new ValueError("'arr.chunk' size must be > 0.");
        var result = new StashArray();
        for (int i = 0; i < array.Count; i += (int)size)
        {
            int chunkSize = Math.Min((int)size, array.Count - i);
            result.Add(StashValue.FromObj(new StashArray(array.GetRange(i, chunkSize))));
        }
        return result;
    }

    /// <summary>Randomly reorders the array in place using Fisher-Yates. Mutates the original array.</summary>
    /// <param name="array">The array to shuffle</param>
    /// <exception cref="TypeError">if `array` is not an array</exception>
    /// <returns>null</returns>
    [StashFn(ReturnType = "null")]
    private static void Shuffle(IInterpreterContext ctx, StashValue array)
    {
        if (array.IsObj && IsArrayFrozen(array.AsObj))
            throw new ReadOnlyError("Cannot mutate a frozen array.");
        if (array.IsObj && array.AsObj is StashTypedArray taShuffle)
        {
            var rng = new Random();
            for (int i = taShuffle.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                StashValue tmp = taShuffle.Get(i);
                taShuffle.Set(i, taShuffle.Get(j));
                taShuffle.Set(j, tmp);
            }
            return;
        }
        if (array.IsObj && array.AsObj is List<StashValue> list)
        {
            var rng = new Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return;
        }
        throw new TypeError("First argument to 'arr.shuffle' must be an array.");
    }

    /// <summary>Returns a new array with the first n elements.</summary>
    /// <param name="array">The source array</param>
    /// <param name="n">The number of elements to take</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>A new array containing the first n elements</returns>
    // Raw = true: inspects args[0] for StashTypedArray to preserve element type in the
    // returned array. The typed form would receive a materialized List<StashValue> and
    // the element type information would be lost.
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Take(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string? elementType = args[0].IsObj && args[0].AsObj is StashTypedArray srcTk ? srcTk.ElementTypeName : null;
        var list = SvArgs.StashList(args, 0, "arr.take");
        var n = SvArgs.Long(args, 1, "arr.take");
        int count = (int)Math.Max(0, Math.Min(n, list.Count));
        var result = new StashArray(list.GetRange(0, count));
        return elementType != null
            ? StashValue.FromObj(StashTypedArray.Create(elementType, result))
            : StashValue.FromObj(result);
    }

    /// <summary>Returns a new array with the first n elements removed.</summary>
    /// <param name="array">The source array</param>
    /// <param name="n">The number of elements to skip</param>
    /// <exception cref="TypeError">if any argument has the wrong type</exception>
    /// <returns>A new array with the first n elements omitted</returns>
    // Raw = true: inspects args[0] for StashTypedArray to preserve element type in the
    // returned array. The typed form would receive a materialized List<StashValue> and
    // the element type information would be lost.
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Drop(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string? elementType = args[0].IsObj && args[0].AsObj is StashTypedArray srcDp ? srcDp.ElementTypeName : null;
        var list = SvArgs.StashList(args, 0, "arr.drop");
        var n = SvArgs.Long(args, 1, "arr.drop");
        int skip = (int)Math.Max(0, Math.Min(n, list.Count));
        var result = new StashArray(list.GetRange(skip, list.Count - skip));
        return elementType != null
            ? StashValue.FromObj(StashTypedArray.Create(elementType, result))
            : StashValue.FromObj(result);
    }

    /// <summary>Splits the array into two arrays: elements matching fn and those that don't.</summary>
    /// <param name="array">The source array</param>
    /// <param name="fn">A predicate function that receives each element</param>
    /// <returns>A two-element array: [matching, nonMatching]</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> Partition(IInterpreterContext ctx, List<StashValue> array, IStashCallable fn)
    {
        var matching = new StashArray();
        var nonMatching = new StashArray();
        foreach (StashValue item in array)
        {
            if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
            {
                matching.Add(item);
            }
            else
            {
                nonMatching.Add(item);
            }
        }
        return new StashArray { StashValue.FromObj(matching), StashValue.FromObj(nonMatching) };
    }

    /// <summary>Creates a typed array from a generic array. Validates all elements match the specified type.</summary>
    /// <param name="source">The source array to convert</param>
    /// <param name="elementType">The element type: "int", "float", "string", or "bool"</param>
    /// <returns>A new typed array</returns>
    [StashFn(ReturnType = "array")]
    private static StashValue Typed(IInterpreterContext ctx, List<StashValue> source, string elementType)
    {
        StashTypedArray result = StashTypedArray.Create(elementType, source);
        return StashValue.FromObj(result);
    }

    /// <summary>Converts a typed array to a generic array.</summary>
    /// <param name="source">The typed array to convert</param>
    /// <exception cref="TypeError">if `source` is not a typed array</exception>
    /// <returns>A new generic array with the same elements</returns>
    // Raw = true: requires SvArgs.TypedArray extraction which is not in the typed parameter
    // type table. The function only accepts typed arrays (not plain lists).
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Untyped(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        StashTypedArray ta = SvArgs.TypedArray(args, 0, "arr.untyped");
        var result = new StashArray(ta.Count);
        for (int i = 0; i < ta.Count; i++)
            result.Add(ta.Get(i));
        return StashValue.FromObj(result);
    }

    /// <summary>Returns the element type name of a typed array, or null for generic arrays.</summary>
    /// <param name="source">The array to inspect</param>
    /// <returns>The element type ("int", "float", "string", "bool") or null</returns>
    // Raw = true: accepts both typed and generic arrays, returning null for generic arrays.
    // The typed form would require StashValue passthrough and identical body logic.
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue ElementType(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        StashValue v = args[0];
        if (v.IsObj && v.AsObj is StashTypedArray ta)
            return StashValue.FromObj(ta.ElementTypeName);
        return StashValue.Null;
    }

    /// <summary>Creates a new zero-initialized typed array with the specified size.</summary>
    /// <param name="elementType">The element type: "int", "float", "string", or "bool"</param>
    /// <param name="size">The number of elements (zero-initialized)</param>
    /// <exception cref="ValueError">if `size` is negative</exception>
    /// <returns>A new typed array</returns>
    [StashFn(ReturnType = "array")]
    private static StashValue Create(IInterpreterContext ctx, string elementType, long size)
    {
        if (size < 0) throw new ValueError("Array size cannot be negative.");
        StashTypedArray result = StashTypedArray.CreateWithCapacity(elementType, (int)size);
        return StashValue.FromObj(result);
    }

    /// <summary>Deprecated. Use <c>arr.create</c>. Creates a new zero-initialized typed array with the specified size.</summary>
    /// <param name="elementType">The element type: "int", "float", "string", or "bool"</param>
    /// <param name="size">The number of elements (zero-initialized)</param>
    /// <exception cref="ValueError">if `size` is negative</exception>
    /// <returns>A new typed array</returns>
    [StashFn(ReturnType = "array")]
    [StashDeprecated("arr.create")]
    private static StashValue New(IInterpreterContext ctx, string elementType, long size)
    {
        if (size < 0) throw new ValueError("Array size cannot be negative.");
        StashTypedArray result = StashTypedArray.CreateWithCapacity(elementType, (int)size);
        return StashValue.FromObj(result);
    }

    private static object? ExecuteParMap(IInterpreterContext ctx, List<StashValue> args)
    {
        if (args.Count < 2 || args[1].ToObject() is not IStashCallable callable)
        {
            throw new TypeError("arr.parMap() expects (array, function, [maxConcurrency]).");
        }
        List<StashValue> list;
        if (args[0].ToObject() is List<StashValue> l1) list = l1;
        else throw new TypeError("arr.parMap() expects (array, function, [maxConcurrency]).");

        int maxConcurrency = ParseMaxConcurrency(args, "arr.parMap");
        int count = list.Count;
        var results = new StashValue[count];
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var options = new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency };
        Parallel.For(0, count, options, i =>
        {
            IInterpreterContext? child = null;
            try
            {
                child = ctx.ForkParallel(ctx.CancellationToken);
                results[i] = child.InvokeCallbackDirect(callable, new StashValue[] { list[i] });
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
            finally
            {
                child?.CleanupTrackedProcesses();
            }
        });

        if (!exceptions.IsEmpty)
        {
            Exception first = exceptions.First();
            if (first is RuntimeError re)
            {
                throw re;
            }

            throw new RuntimeError($"arr.parMap() failed: {first.Message}");
        }

        return new StashArray(results);
    }

    private static object? ExecuteParFilter(IInterpreterContext ctx, List<StashValue> args)
    {
        if (args.Count < 2 || args[1].ToObject() is not IStashCallable callable)
        {
            throw new TypeError("arr.parFilter() expects (array, function, [maxConcurrency]).");
        }
        List<StashValue> list;
        if (args[0].ToObject() is List<StashValue> l1) list = l1;
        else throw new TypeError("arr.parFilter() expects (array, function, [maxConcurrency]).");

        int maxConcurrency = ParseMaxConcurrency(args, "arr.parFilter");
        int count = list.Count;
        var keep = new bool[count];
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var options = new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency };
        Parallel.For(0, count, options, i =>
        {
            IInterpreterContext? child = null;
            try
            {
                child = ctx.ForkParallel(ctx.CancellationToken);
                StashValue result = child.InvokeCallbackDirect(callable, new StashValue[] { list[i] });
                keep[i] = RuntimeValues.IsTruthy(result.ToObject());
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
            finally
            {
                child?.CleanupTrackedProcesses();
            }
        });

        if (!exceptions.IsEmpty)
        {
            Exception first = exceptions.First();
            if (first is RuntimeError re)
            {
                throw re;
            }

            throw new RuntimeError($"arr.parFilter() failed: {first.Message}");
        }

        var filtered = new StashArray();
        for (int i = 0; i < count; i++)
        {
            if (keep[i])
            {
                filtered.Add(list[i]);
            }
        }
        return filtered;
    }

    private static object? ExecuteParForEach(IInterpreterContext ctx, List<StashValue> args)
    {
        if (args.Count < 2 || args[1].ToObject() is not IStashCallable callable)
        {
            throw new TypeError("arr.parForEach() expects (array, function, [maxConcurrency]).");
        }
        List<StashValue> list;
        if (args[0].ToObject() is List<StashValue> l1) list = l1;
        else throw new TypeError("arr.parForEach() expects (array, function, [maxConcurrency]).");

        int maxConcurrency = ParseMaxConcurrency(args, "arr.parForEach");
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var options = new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency };
        Parallel.For(0, list.Count, options, i =>
        {
            IInterpreterContext? child = null;
            try
            {
                child = ctx.ForkParallel(ctx.CancellationToken);
                child.InvokeCallbackDirect(callable, new StashValue[] { list[i] });
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
            finally
            {
                child?.CleanupTrackedProcesses();
            }
        });

        if (!exceptions.IsEmpty)
        {
            Exception first = exceptions.First();
            if (first is RuntimeError re)
            {
                throw re;
            }

            throw new RuntimeError($"arr.parForEach() failed: {first.Message}");
        }

        return null;
    }

    private static int ParseMaxConcurrency(List<StashValue> args, string fnName)
    {
        if (args.Count < 3 || args[2].IsNull)
        {
            return -1; // -1 means unlimited (use all available cores)
        }

        if (args[2].ToObject() is long n)
        {
            if (n < 1)
            {
                throw new ValueError($"Third argument to '{fnName}' (maxConcurrency) must be >= 1.");
            }

            return (int)n;
        }

        throw new TypeError($"Third argument to '{fnName}' (maxConcurrency) must be an integer.");
    }

    private static void FlattenInto(List<StashValue> source, List<StashValue> result, int depth)
    {
        foreach (var item in source)
        {
            if (depth != 0 && item.AsObj is List<StashValue> inner)
                FlattenInto(inner, result, depth == -1 ? -1 : depth - 1);
            else
                result.Add(item);
        }
    }

    private static int CompareValues(object? a, object? b)
    {
        if (a is long la && b is long lb)
        {
            return la.CompareTo(lb);
        }

        if (a is double da && b is double db)
        {
            return da.CompareTo(db);
        }

        if (a is long la2 && b is double db2)
        {
            return ((double)la2).CompareTo(db2);
        }

        if (a is double da2 && b is long lb2)
        {
            return da2.CompareTo((double)lb2);
        }

        if (a is string sa && b is string sb)
        {
            return string.Compare(sa, sb, StringComparison.Ordinal);
        }

        throw new TypeError("Cannot compare values of incompatible types in 'arr.sortBy'.");
    }

    /// <summary>
    /// Returns true if <paramref name="obj"/> is a frozen <see cref="StashArray"/>.
    /// </summary>
    private static bool IsArrayFrozen(object? obj) =>
        obj is StashArray sa && sa.IsFrozen;
}
