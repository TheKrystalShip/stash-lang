namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;

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

        globals.Define("arr", arr);
    }
}
