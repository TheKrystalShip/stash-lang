namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'arr' namespace built-in functions.
/// </summary>
public static class ArrBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        // ── arr namespace ────────────────────────────────────────────────
        var arr = new StashNamespace("arr");

        arr.Define("push", new BuiltInFunction("arr.push", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.push' must be an array.");
            }

            list.Add(args[1]);
            return null;
        }));

        arr.Define("pop", new BuiltInFunction("arr.pop", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.pop' must be an array.");
            }

            if (list.Count == 0)
            {
                throw new RuntimeError("Cannot pop from an empty array.");
            }

            var last = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return last;
        }));

        arr.Define("peek", new BuiltInFunction("arr.peek", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.peek' must be an array.");
            }

            if (list.Count == 0)
            {
                throw new RuntimeError("Cannot peek an empty array.");
            }

            return list[list.Count - 1];
        }));

        arr.Define("insert", new BuiltInFunction("arr.insert", 3, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.insert' must be an array.");
            }

            if (args[1] is not long idx)
            {
                throw new RuntimeError("Second argument to 'arr.insert' must be an integer.");
            }

            if (idx < 0 || idx > list.Count)
            {
                throw new RuntimeError($"Index {idx} is out of bounds for 'arr.insert'.");
            }

            list.Insert((int)idx, args[2]);
            return null;
        }));

        arr.Define("removeAt", new BuiltInFunction("arr.removeAt", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.removeAt' must be an array.");
            }

            if (args[1] is not long idx)
            {
                throw new RuntimeError("Second argument to 'arr.removeAt' must be an integer.");
            }

            if (idx < 0 || idx >= list.Count)
            {
                throw new RuntimeError($"Index {idx} is out of bounds for 'arr.removeAt'.");
            }

            var removed = list[(int)idx];
            list.RemoveAt((int)idx);
            return removed;
        }));

        arr.Define("remove", new BuiltInFunction("arr.remove", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.remove' must be an array.");
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (RuntimeValues.IsEqual(list[i], args[1]))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }));

        arr.Define("clear", new BuiltInFunction("arr.clear", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.clear' must be an array.");
            }

            list.Clear();
            return null;
        }));

        arr.Define("contains", new BuiltInFunction("arr.contains", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.contains' must be an array.");
            }

            foreach (var item in list)
            {
                if (RuntimeValues.IsEqual(item, args[1]))
                {
                    return true;
                }
            }
            return false;
        }));

        arr.Define("indexOf", new BuiltInFunction("arr.indexOf", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.indexOf' must be an array.");
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (RuntimeValues.IsEqual(list[i], args[1]))
                {
                    return (long)i;
                }
            }
            return -1L;
        }));

        arr.Define("slice", new BuiltInFunction("arr.slice", 3, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.slice' must be an array.");
            }

            if (args[1] is not long start)
            {
                throw new RuntimeError("Second argument to 'arr.slice' must be an integer.");
            }

            if (args[2] is not long end)
            {
                throw new RuntimeError("Third argument to 'arr.slice' must be an integer.");
            }

            int s = (int)Math.Max(0, Math.Min(start, list.Count));
            int e = (int)Math.Max(0, Math.Min(end, list.Count));
            if (e < s)
            {
                e = s;
            }

            return list.GetRange(s, e - s);
        }));

        arr.Define("concat", new BuiltInFunction("arr.concat", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list1)
            {
                throw new RuntimeError("First argument to 'arr.concat' must be an array.");
            }

            if (args[1] is not List<object?> list2)
            {
                throw new RuntimeError("Second argument to 'arr.concat' must be an array.");
            }

            var result = new List<object?>(list1);
            result.AddRange(list2);
            return result;
        }));

        arr.Define("join", new BuiltInFunction("arr.join", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.join' must be an array.");
            }

            if (args[1] is not string sep)
            {
                throw new RuntimeError("Second argument to 'arr.join' must be a string.");
            }

            var parts = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                parts[i] = RuntimeValues.Stringify(list[i]);
            }

            return string.Join(sep, parts);
        }));

        arr.Define("reverse", new BuiltInFunction("arr.reverse", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.reverse' must be an array.");
            }

            list.Reverse();
            return null;
        }));

        arr.Define("sort", new BuiltInFunction("arr.sort", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.sort' must be an array.");
            }

            try
            {
                list.Sort((a, b) =>
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

                    throw new RuntimeError("Cannot compare values of incompatible types in 'arr.sort'.");
                });
            }
            catch (InvalidOperationException ex) when (ex.InnerException is RuntimeError re)
            {
                throw re;
            }
            return null;
        }));

        arr.Define("map", new BuiltInFunction("arr.map", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.map' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.map' must be a function.");
            }

            var result = new List<object?>();
            foreach (var item in list)
            {
                result.Add(fn.Call(interp, new List<object?> { item }));
            }

            return result;
        }));

        arr.Define("filter", new BuiltInFunction("arr.filter", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.filter' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.filter' must be a function.");
            }

            var result = new List<object?>();
            foreach (var item in list)
            {
                if (RuntimeValues.IsTruthy(fn.Call(interp, new List<object?> { item })))
                {
                    result.Add(item);
                }
            }
            return result;
        }));

        arr.Define("forEach", new BuiltInFunction("arr.forEach", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.forEach' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.forEach' must be a function.");
            }

            foreach (var item in list)
            {
                fn.Call(interp, new List<object?> { item });
            }

            return null;
        }));

        arr.Define("find", new BuiltInFunction("arr.find", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.find' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.find' must be a function.");
            }

            foreach (var item in list)
            {
                if (RuntimeValues.IsTruthy(fn.Call(interp, new List<object?> { item })))
                {
                    return item;
                }
            }
            return null;
        }));

        arr.Define("reduce", new BuiltInFunction("arr.reduce", 3, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.reduce' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.reduce' must be a function.");
            }

            var accumulator = args[2];
            foreach (var item in list)
            {
                accumulator = fn.Call(interp, new List<object?> { accumulator, item });
            }

            return accumulator;
        }));

        arr.Define("unique", new BuiltInFunction("arr.unique", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.unique' must be an array.");
            }

            var result = new List<object?>();
            foreach (var item in list)
            {
                bool found = false;
                foreach (var existing in result)
                {
                    if (RuntimeValues.IsEqual(item, existing))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    result.Add(item);
                }
            }
            return result;
        }));

        arr.Define("any", new BuiltInFunction("arr.any", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.any' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.any' must be a function.");
            }

            foreach (var item in list)
            {
                if (RuntimeValues.IsTruthy(fn.Call(interp, new List<object?> { item })))
                {
                    return true;
                }
            }
            return false;
        }));

        arr.Define("every", new BuiltInFunction("arr.every", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.every' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.every' must be a function.");
            }

            foreach (var item in list)
            {
                if (!RuntimeValues.IsTruthy(fn.Call(interp, new List<object?> { item })))
                {
                    return false;
                }
            }
            return true;
        }));

        arr.Define("flat", new BuiltInFunction("arr.flat", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.flat' must be an array.");
            }

            var result = new List<object?>();
            foreach (var item in list)
            {
                if (item is List<object?> inner)
                {
                    result.AddRange(inner);
                }
                else
                {
                    result.Add(item);
                }
            }
            return result;
        }));

        arr.Define("flatMap", new BuiltInFunction("arr.flatMap", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.flatMap' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.flatMap' must be a function.");
            }

            var result = new List<object?>();
            foreach (var item in list)
            {
                var mapped = fn.Call(interp, new List<object?> { item });
                if (mapped is List<object?> inner)
                {
                    result.AddRange(inner);
                }
                else
                {
                    result.Add(mapped);
                }
            }
            return result;
        }));

        arr.Define("findIndex", new BuiltInFunction("arr.findIndex", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.findIndex' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.findIndex' must be a function.");
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (RuntimeValues.IsTruthy(fn.Call(interp, new List<object?> { list[i] })))
                {
                    return (long)i;
                }
            }
            return -1L;
        }));

        arr.Define("count", new BuiltInFunction("arr.count", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.count' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.count' must be a function.");
            }

            long count = 0;
            foreach (var item in list)
            {
                if (RuntimeValues.IsTruthy(fn.Call(interp, new List<object?> { item })))
                {
                    count++;
                }
            }
            return count;
        }));

        arr.Define("sortBy", new BuiltInFunction("arr.sortBy", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.sortBy' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.sortBy' must be a function.");
            }

            var sorted = new List<object?>(list);
            try
            {
                sorted.Sort((a, b) =>
                {
                    var ka = fn.Call(interp, new List<object?> { a });
                    var kb = fn.Call(interp, new List<object?> { b });
                    return CompareValues(ka, kb);
                });
            }
            catch (InvalidOperationException ex) when (ex.InnerException is RuntimeError re)
            {
                throw re;
            }
            return sorted;
        }));

        arr.Define("groupBy", new BuiltInFunction("arr.groupBy", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.groupBy' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.groupBy' must be a function.");
            }

            var result = new StashDictionary();
            foreach (var item in list)
            {
                var key = RuntimeValues.Stringify(fn.Call(interp, new List<object?> { item }));
                var existing = result.Get(key);
                if (existing is List<object?> group)
                {
                    group.Add(item);
                }
                else
                {
                    result.Set(key, new List<object?> { item });
                }
            }
            return result;
        }));

        arr.Define("sum", new BuiltInFunction("arr.sum", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.sum' must be an array.");
            }

            long longTotal = 0;
            double doubleTotal = 0;
            bool hasFloat = false;
            foreach (var item in list)
            {
                if (item is long l)
                {
                    longTotal += l;
                }
                else if (item is double d) { doubleTotal += d; hasFloat = true; }
                else
                {
                    throw new RuntimeError("'arr.sum' requires all elements to be numbers.");
                }
            }
            return hasFloat ? (object?)(doubleTotal + longTotal) : (object?)longTotal;
        }));

        arr.Define("min", new BuiltInFunction("arr.min", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.min' must be an array.");
            }

            if (list.Count == 0)
            {
                throw new RuntimeError("'arr.min' requires a non-empty array.");
            }

            double min = double.MaxValue;
            bool hasFloat = false;
            foreach (var item in list)
            {
                if (item is long l)
                {
                    if (l < min)
                    {
                        min = l;
                    }
                }
                else if (item is double d) { if (d < min) { min = d; } hasFloat = true; }
                else
                {
                    throw new RuntimeError("'arr.min' requires all elements to be numbers.");
                }
            }
            return hasFloat ? min : (object)(long)min;
        }));

        arr.Define("max", new BuiltInFunction("arr.max", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.max' must be an array.");
            }

            if (list.Count == 0)
            {
                throw new RuntimeError("'arr.max' requires a non-empty array.");
            }

            double max = double.MinValue;
            bool hasFloat = false;
            foreach (var item in list)
            {
                if (item is long l)
                {
                    if (l > max)
                    {
                        max = l;
                    }
                }
                else if (item is double d) { if (d > max) { max = d; } hasFloat = true; }
                else
                {
                    throw new RuntimeError("'arr.max' requires all elements to be numbers.");
                }
            }
            return hasFloat ? max : (object)(long)max;
        }));

        // ── Additional array utilities ───────────────────────────────────
        arr.Define("zip", new BuiltInFunction("arr.zip", 2, (_, args) =>
        {
            if (args[0] is not List<object?> a)
            {
                throw new RuntimeError("First argument to 'arr.zip' must be an array.");
            }

            if (args[1] is not List<object?> b)
            {
                throw new RuntimeError("Second argument to 'arr.zip' must be an array.");
            }

            int len = Math.Min(a.Count, b.Count);
            var result = new List<object?>(len);
            for (int i = 0; i < len; i++)
            {
                result.Add(new List<object?> { a[i], b[i] });
            }

            return result;
        }));

        arr.Define("chunk", new BuiltInFunction("arr.chunk", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.chunk' must be an array.");
            }

            if (args[1] is not long size)
            {
                throw new RuntimeError("Second argument to 'arr.chunk' must be an integer.");
            }

            if (size <= 0)
            {
                throw new RuntimeError("'arr.chunk' size must be > 0.");
            }

            var result = new List<object?>();
            for (int i = 0; i < list.Count; i += (int)size)
            {
                int chunkSize = Math.Min((int)size, list.Count - i);
                result.Add(list.GetRange(i, chunkSize));
            }
            return result;
        }));

        arr.Define("shuffle", new BuiltInFunction("arr.shuffle", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.shuffle' must be an array.");
            }

            var rng = new Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return null;
        }));

        arr.Define("take", new BuiltInFunction("arr.take", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.take' must be an array.");
            }

            if (args[1] is not long n)
            {
                throw new RuntimeError("Second argument to 'arr.take' must be an integer.");
            }

            int count = (int)Math.Max(0, Math.Min(n, list.Count));
            return list.GetRange(0, count);
        }));

        arr.Define("drop", new BuiltInFunction("arr.drop", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.drop' must be an array.");
            }

            if (args[1] is not long n)
            {
                throw new RuntimeError("Second argument to 'arr.drop' must be an integer.");
            }

            int skip = (int)Math.Max(0, Math.Min(n, list.Count));
            return list.GetRange(skip, list.Count - skip);
        }));

        arr.Define("partition", new BuiltInFunction("arr.partition", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
            {
                throw new RuntimeError("First argument to 'arr.partition' must be an array.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'arr.partition' must be a function.");
            }

            var matching = new List<object?>();
            var nonMatching = new List<object?>();
            foreach (var item in list)
            {
                if (RuntimeValues.IsTruthy(fn.Call(interp, new List<object?> { item })))
                {
                    matching.Add(item);
                }
                else
                {
                    nonMatching.Add(item);
                }
            }
            return new List<object?> { matching, nonMatching };
        }));

        globals.Define("arr", arr);
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

        throw new RuntimeError("Cannot compare values of incompatible types in 'arr.sortBy'.");
    }
}
