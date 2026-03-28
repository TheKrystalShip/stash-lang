namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the 'arr' namespace built-in functions.
/// </summary>
/// <remarks>
/// <para>
/// Provides functions including <c>arr.push</c>, <c>arr.pop</c>, <c>arr.map</c>,
/// <c>arr.filter</c>, <c>arr.sort</c>, <c>arr.reduce</c>, and more.
/// All functions are registered as <see cref="BuiltInFunction"/> instances on a
/// <see cref="StashNamespace"/> in the global <see cref="Stash.Interpreting.Environment"/>.
/// </para>
/// </remarks>
public static class ArrBuiltIns
{
    /// <summary>
    /// Registers all <c>arr</c> namespace functions into the global environment.
    /// </summary>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("arr");

        ns.Function("push", [Param("array", "array"), Param("value")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.push");
            list.Add(args[1]);
            return null;
        });

        ns.Function("pop", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.pop");
            if (list.Count == 0)
            {
                throw new RuntimeError("Cannot pop from an empty array.");
            }

            var last = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return last;
        });

        ns.Function("peek", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.peek");
            if (list.Count == 0)
            {
                throw new RuntimeError("Cannot peek an empty array.");
            }

            return list[list.Count - 1];
        });

        ns.Function("insert", [Param("array", "array"), Param("index", "int"), Param("value")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.insert");
            var idx = Args.Long(args, 1, "arr.insert");
            if (idx < 0 || idx > list.Count)
            {
                throw new RuntimeError($"Index {idx} is out of bounds for 'arr.insert'.");
            }

            list.Insert((int)idx, args[2]);
            return null;
        });

        ns.Function("removeAt", [Param("array", "array"), Param("index", "int")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.removeAt");
            var idx = Args.Long(args, 1, "arr.removeAt");
            if (idx < 0 || idx >= list.Count)
            {
                throw new RuntimeError($"Index {idx} is out of bounds for 'arr.removeAt'.");
            }

            var removed = list[(int)idx];
            list.RemoveAt((int)idx);
            return removed;
        });

        ns.Function("remove", [Param("array", "array"), Param("value")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.remove");
            for (int i = 0; i < list.Count; i++)
            {
                if (RuntimeValues.IsEqual(list[i], args[1]))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        });

        ns.Function("clear", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.clear");
            list.Clear();
            return null;
        });

        ns.Function("contains", [Param("array", "array"), Param("value")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.contains");
            foreach (var item in list)
            {
                if (RuntimeValues.IsEqual(item, args[1]))
                {
                    return true;
                }
            }
            return false;
        });

        ns.Function("indexOf", [Param("array", "array"), Param("value")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.indexOf");
            for (int i = 0; i < list.Count; i++)
            {
                if (RuntimeValues.IsEqual(list[i], args[1]))
                {
                    return (long)i;
                }
            }
            return -1L;
        });

        ns.Function("slice", [Param("array", "array"), Param("start", "int"), Param("end", "int")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.slice");
            var start = Args.Long(args, 1, "arr.slice");
            var end = Args.Long(args, 2, "arr.slice");
            int s = (int)Math.Max(0, Math.Min(start, list.Count));
            int e = (int)Math.Max(0, Math.Min(end, list.Count));
            if (e < s)
            {
                e = s;
            }

            return list.GetRange(s, e - s);
        });

        ns.Function("concat", [Param("a", "array"), Param("b", "array")], (_, args) =>
        {
            var list1 = Args.List(args, 0, "arr.concat");
            var list2 = Args.List(args, 1, "arr.concat");
            var result = new List<object?>(list1);
            result.AddRange(list2);
            return result;
        });

        ns.Function("join", [Param("array", "array"), Param("separator", "string")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.join");
            var sep = Args.String(args, 1, "arr.join");
            var parts = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                parts[i] = RuntimeValues.Stringify(list[i]);
            }

            return string.Join(sep, parts);
        });

        ns.Function("reverse", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.reverse");
            list.Reverse();
            return null;
        });

        ns.Function("sort", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.sort");
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
        });

        ns.Function("map", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.map");
            var fn = Args.Callable(args, 1, "arr.map");
            var result = new List<object?>();
            foreach (var item in list)
            {
                result.Add(fn.Call(ctx, new List<object?> { item }));
            }

            return result;
        });

        ns.Function("filter", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.filter");
            var fn = Args.Callable(args, 1, "arr.filter");
            var result = new List<object?>();
            foreach (var item in list)
            {
                if (RuntimeValues.IsTruthy(fn.Call(ctx, new List<object?> { item })))
                {
                    result.Add(item);
                }
            }
            return result;
        });

        ns.Function("forEach", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.forEach");
            var fn = Args.Callable(args, 1, "arr.forEach");
            foreach (var item in list)
            {
                fn.Call(ctx, new List<object?> { item });
            }

            return null;
        });

        ns.Function("parMap", [Param("array", "array"), Param("fn", "function")], ParMap, isVariadic: true);
        ns.Function("parFilter", [Param("array", "array"), Param("fn", "function")], ParFilter, isVariadic: true);
        ns.Function("parForEach", [Param("array", "array"), Param("fn", "function")], ParForEach, isVariadic: true);

        ns.Function("find", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.find");
            var fn = Args.Callable(args, 1, "arr.find");
            foreach (var item in list)
            {
                if (RuntimeValues.IsTruthy(fn.Call(ctx, new List<object?> { item })))
                {
                    return item;
                }
            }
            return null;
        });

        ns.Function("reduce", [Param("array", "array"), Param("fn", "function"), Param("initial")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.reduce");
            var fn = Args.Callable(args, 1, "arr.reduce");
            var accumulator = args[2];
            foreach (var item in list)
            {
                accumulator = fn.Call(ctx, new List<object?> { accumulator, item });
            }

            return accumulator;
        });

        ns.Function("unique", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.unique");
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
        });

        ns.Function("any", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.any");
            var fn = Args.Callable(args, 1, "arr.any");
            foreach (var item in list)
            {
                if (RuntimeValues.IsTruthy(fn.Call(ctx, new List<object?> { item })))
                {
                    return true;
                }
            }
            return false;
        });

        ns.Function("every", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.every");
            var fn = Args.Callable(args, 1, "arr.every");
            foreach (var item in list)
            {
                if (!RuntimeValues.IsTruthy(fn.Call(ctx, new List<object?> { item })))
                {
                    return false;
                }
            }
            return true;
        });

        ns.Function("flat", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.flat");
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
        });

        ns.Function("flatMap", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.flatMap");
            var fn = Args.Callable(args, 1, "arr.flatMap");
            var result = new List<object?>();
            foreach (var item in list)
            {
                var mapped = fn.Call(ctx, new List<object?> { item });
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
        });

        ns.Function("findIndex", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.findIndex");
            var fn = Args.Callable(args, 1, "arr.findIndex");
            for (int i = 0; i < list.Count; i++)
            {
                if (RuntimeValues.IsTruthy(fn.Call(ctx, new List<object?> { list[i] })))
                {
                    return (long)i;
                }
            }
            return -1L;
        });

        ns.Function("count", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.count");
            var fn = Args.Callable(args, 1, "arr.count");
            long count = 0;
            foreach (var item in list)
            {
                if (RuntimeValues.IsTruthy(fn.Call(ctx, new List<object?> { item })))
                {
                    count++;
                }
            }
            return count;
        });

        ns.Function("sortBy", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.sortBy");
            var fn = Args.Callable(args, 1, "arr.sortBy");
            var sorted = new List<object?>(list);
            try
            {
                sorted.Sort((a, b) =>
                {
                    var ka = fn.Call(ctx, new List<object?> { a });
                    var kb = fn.Call(ctx, new List<object?> { b });
                    return CompareValues(ka, kb);
                });
            }
            catch (InvalidOperationException ex) when (ex.InnerException is RuntimeError re)
            {
                throw re;
            }
            return sorted;
        });

        ns.Function("groupBy", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.groupBy");
            var fn = Args.Callable(args, 1, "arr.groupBy");
            var result = new StashDictionary();
            foreach (var item in list)
            {
                var key = RuntimeValues.Stringify(fn.Call(ctx, new List<object?> { item }));
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
        });

        ns.Function("sum", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.sum");
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
        });

        ns.Function("min", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.min");
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
        });

        ns.Function("max", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.max");
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
        });

        // ── Additional array utilities ───────────────────────────────────
        ns.Function("zip", [Param("a", "array"), Param("b", "array")], (_, args) =>
        {
            var a = Args.List(args, 0, "arr.zip");
            var list2 = Args.List(args, 1, "arr.zip");
            int len = Math.Min(a.Count, list2.Count);
            var result = new List<object?>(len);
            for (int i = 0; i < len; i++)
            {
                result.Add(new List<object?> { a[i], list2[i] });
            }

            return result;
        });

        ns.Function("chunk", [Param("array", "array"), Param("size", "int")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.chunk");
            var size = Args.Long(args, 1, "arr.chunk");
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
        });

        ns.Function("shuffle", [Param("array", "array")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.shuffle");
            var rng = new Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return null;
        });

        ns.Function("take", [Param("array", "array"), Param("n", "int")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.take");
            var n = Args.Long(args, 1, "arr.take");
            int count = (int)Math.Max(0, Math.Min(n, list.Count));
            return list.GetRange(0, count);
        });

        ns.Function("drop", [Param("array", "array"), Param("n", "int")], (_, args) =>
        {
            var list = Args.List(args, 0, "arr.drop");
            var n = Args.Long(args, 1, "arr.drop");
            int skip = (int)Math.Max(0, Math.Min(n, list.Count));
            return list.GetRange(skip, list.Count - skip);
        });

        ns.Function("partition", [Param("array", "array"), Param("fn", "function")], (ctx, args) =>
        {
            var list = Args.List(args, 0, "arr.partition");
            var fn = Args.Callable(args, 1, "arr.partition");
            var matching = new List<object?>();
            var nonMatching = new List<object?>();
            foreach (var item in list)
            {
                if (RuntimeValues.IsTruthy(fn.Call(ctx, new List<object?> { item })))
                {
                    matching.Add(item);
                }
                else
                {
                    nonMatching.Add(item);
                }
            }
            return new List<object?> { matching, nonMatching };
        });

        return ns.Build();
    }

    private static object? ParMap(IInterpreterContext ctx, List<object?> args)
    {
        if (args.Count < 2 || args[0] is not List<object?> list || args[1] is not IStashCallable callable)
        {
            throw new RuntimeError("arr.parMap() expects (array, function, [maxConcurrency]).");
        }

        int maxConcurrency = ParseMaxConcurrency(args, "arr.parMap");
        int count = list.Count;
        var results = new object?[count];
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var options = new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency };
        Parallel.For(0, count, options, i =>
        {
            IInterpreterContext? child = null;
            try
            {
                child = ctx.ForkParallel(ctx.CancellationToken);
                results[i] = callable.Call(child, new List<object?> { list[i] });
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

        return new List<object?>(results);
    }

    private static object? ParFilter(IInterpreterContext ctx, List<object?> args)
    {
        if (args.Count < 2 || args[0] is not List<object?> list || args[1] is not IStashCallable callable)
        {
            throw new RuntimeError("arr.parFilter() expects (array, function, [maxConcurrency]).");
        }

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
                object? result = callable.Call(child, new List<object?> { list[i] });
                keep[i] = RuntimeValues.IsTruthy(result);
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

        var filtered = new List<object?>();
        for (int i = 0; i < count; i++)
        {
            if (keep[i])
            {
                filtered.Add(list[i]);
            }
        }
        return filtered;
    }

    private static object? ParForEach(IInterpreterContext ctx, List<object?> args)
    {
        if (args.Count < 2 || args[0] is not List<object?> list || args[1] is not IStashCallable callable)
        {
            throw new RuntimeError("arr.parForEach() expects (array, function, [maxConcurrency]).");
        }

        int maxConcurrency = ParseMaxConcurrency(args, "arr.parForEach");
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var options = new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency };
        Parallel.For(0, list.Count, options, i =>
        {
            IInterpreterContext? child = null;
            try
            {
                child = ctx.ForkParallel(ctx.CancellationToken);
                callable.Call(child, new List<object?> { list[i] });
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

    private static int ParseMaxConcurrency(List<object?> args, string fnName)
    {
        if (args.Count < 3 || args[2] is null)
        {
            return -1; // -1 means unlimited (use all available cores)
        }

        if (args[2] is long n)
        {
            if (n < 1)
            {
                throw new RuntimeError($"Third argument to '{fnName}' (maxConcurrency) must be >= 1.");
            }

            return (int)n;
        }

        throw new RuntimeError($"Third argument to '{fnName}' (maxConcurrency) must be an integer.");
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
