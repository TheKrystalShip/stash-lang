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

        ns.Function("push", [Param("array", "array"), Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            SvArgs.StashList(args, 0, "arr.push").Add(args[1]);
            return StashValue.Null;
        });

        ns.Function("pop", [Param("array", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.pop");
            if (list.Count == 0)
                throw new RuntimeError("Cannot pop from an empty array.");
            var last = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return last;
        });

        ns.Function("peek", [Param("array", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.peek");
            if (list.Count == 0)
                throw new RuntimeError("Cannot peek an empty array.");
            return list[list.Count - 1];
        });

        ns.Function("insert", [Param("array", "array"), Param("index", "int"), Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.insert");
            var idx = SvArgs.Long(args, 1, "arr.insert");
            if (idx < 0 || idx > list.Count)
                throw new RuntimeError($"Index {idx} is out of bounds for 'arr.insert'.");
            list.Insert((int)idx, args[2]);
            return StashValue.Null;
        });

        ns.Function("removeAt", [Param("array", "array"), Param("index", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.removeAt");
            var idx = SvArgs.Long(args, 1, "arr.removeAt");
            if (idx < 0 || idx >= list.Count)
                throw new RuntimeError($"Index {idx} is out of bounds for 'arr.removeAt'.");
            var removed = list[(int)idx];
            list.RemoveAt((int)idx);
            return removed;
        });

        ns.Function("remove", [Param("array", "array"), Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.remove");
            var value = args[1].ToObject();
            for (int i = 0; i < list.Count; i++)
            {
                if (RuntimeValues.IsEqual(list[i].ToObject(), value))
                {
                    list.RemoveAt(i);
                    return StashValue.True;
                }
            }
            return StashValue.False;
        });

        ns.Function("clear", [Param("array", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            SvArgs.StashList(args, 0, "arr.clear").Clear();
            return StashValue.Null;
        });

        ns.Function("contains", [Param("array", "array"), Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.contains");
            var value = args[1].ToObject();
            foreach (var item in list)
            {
                if (RuntimeValues.IsEqual(item.ToObject(), value))
                    return StashValue.True;
            }
            return StashValue.False;
        });

        ns.Function("includes", [Param("array", "array"), Param("value"), Param("startIndex?", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'arr.includes' requires 2 or 3 arguments.");
            var list = SvArgs.StashList(args, 0, "arr.includes");
            var value = args[1].ToObject();
            int start = args.Length == 3 ? (int)SvArgs.Long(args, 2, "arr.includes") : 0;
            if (start < 0) start = Math.Max(0, list.Count + start);
            for (int i = start; i < list.Count; i++)
            {
                if (RuntimeValues.IsEqual(list[i].ToObject(), value))
                    return StashValue.True;
            }
            return StashValue.False;
        },
            isVariadic: true,
            documentation: "Returns true if the array contains the given value. Optional startIndex (default 0) sets the starting search position.");

        ns.Function("indexOf", [Param("array", "array"), Param("value"), Param("startIndex?", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'arr.indexOf' requires 2 or 3 arguments.");
            var list = SvArgs.StashList(args, 0, "arr.indexOf");
            var value = args[1].ToObject();
            int start = args.Length == 3 ? (int)SvArgs.Long(args, 2, "arr.indexOf") : 0;
            if (start < 0) start = Math.Max(0, list.Count + start);
            for (int i = start; i < list.Count; i++)
            {
                if (RuntimeValues.IsEqual(list[i].ToObject(), value))
                    return StashValue.FromInt((long)i);
            }
            return StashValue.FromInt(-1L);
        },
            isVariadic: true,
            documentation: "Returns the index of the first occurrence of value in the array. Optional startIndex (default 0) sets the starting search position. Returns -1 if not found.");

        ns.Function("lastIndexOf", [Param("array", "array"), Param("value"), Param("startIndex?", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            isVariadic: true,
            documentation: "Returns the index of the last occurrence of value in the array, searching backwards from the end (or from startIndex if provided). Returns -1 if not found.");

        ns.Function("slice", [Param("array", "array"), Param("start", "int"), Param("end", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.slice");
            var start = SvArgs.Long(args, 1, "arr.slice");
            var end = SvArgs.Long(args, 2, "arr.slice");
            int s = (int)Math.Max(0, Math.Min(start, list.Count));
            int e = (int)Math.Max(0, Math.Min(end, list.Count));
            if (e < s) e = s;
            return StashValue.FromObj(list.GetRange(s, e - s));
        });

        ns.Function("concat", [Param("a", "array"), Param("b", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list1 = SvArgs.StashList(args, 0, "arr.concat");
            var list2 = SvArgs.StashList(args, 1, "arr.concat");
            var result = new List<StashValue>(list1.Count + list2.Count);
            result.AddRange(list1);
            result.AddRange(list2);
            return StashValue.FromObj(result);
        });

        ns.Function("join", [Param("array", "array"), Param("separator?", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'arr.join' requires 1 or 2 arguments.");
            var list = SvArgs.StashList(args, 0, "arr.join");
            string sep = args.Length == 2 ? SvArgs.String(args, 1, "arr.join") : ",";
            var parts = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
                parts[i] = RuntimeValues.Stringify(list[i].ToObject());
            return StashValue.FromObj(string.Join(sep, parts));
        },
            isVariadic: true,
            documentation: "Joins array elements into a string. Optional separator defaults to \",\".");

        ns.Function("reverse", [Param("array", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            SvArgs.StashList(args, 0, "arr.reverse").Reverse();
            return StashValue.Null;
        });

        ns.Function("sort", [Param("array", "array"), Param("comparator?", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'arr.sort' requires 1 or 2 arguments.");
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
                        throw new RuntimeError("'arr.sort' comparator must return a number.");
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
                        throw new RuntimeError("Cannot compare values of incompatible types in 'arr.sort'.");
                    });
                }
            }
            catch (InvalidOperationException ex) when (ex.InnerException is RuntimeError re)
            {
                throw re;
            }
            return StashValue.Null;
        },
            isVariadic: true,
            documentation: "Sorts the array in place. When comparator is provided, it receives two elements and must return a negative number (a < b), zero (a == b), or positive number (a > b).");

        ns.Function("map", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.map");
            var fn = SvArgs.Callable(args, 1, "arr.map");
            var result = new List<StashValue>(list.Count);
            foreach (StashValue item in list)
            {
                StashValue mapped = ctx.InvokeCallbackDirect(fn, new StashValue[] { item });
                result.Add(mapped);
            }
            return StashValue.FromObj(result);
        });

        ns.Function("filter", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.filter");
            var fn = SvArgs.Callable(args, 1, "arr.filter");
            var result = new List<StashValue>(list.Count);
            foreach (StashValue item in list)
            {
                if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
                {
                    result.Add(item);
                }
            }
            return StashValue.FromObj(result);
        });

        ns.Function("forEach", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.forEach");
            var fn = SvArgs.Callable(args, 1, "arr.forEach");
            foreach (StashValue item in list)
            {
                ctx.InvokeCallbackDirect(fn, new StashValue[] { item });
            }
            return StashValue.Null;
        });

        ns.Function("parMap", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = new System.Collections.Generic.List<StashValue>(args.Length);
            foreach (StashValue sv in args) list.Add(sv);
            return StashValue.FromObject(ParMap(ctx, list));
        }, isVariadic: true);
        ns.Function("parFilter", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = new System.Collections.Generic.List<StashValue>(args.Length);
            foreach (StashValue sv in args) list.Add(sv);
            return StashValue.FromObject(ParFilter(ctx, list));
        }, isVariadic: true);
        ns.Function("parForEach", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = new System.Collections.Generic.List<StashValue>(args.Length);
            foreach (StashValue sv in args) list.Add(sv);
            return StashValue.FromObject(ParForEach(ctx, list));
        }, isVariadic: true);

        ns.Function("find", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.find");
            var fn = SvArgs.Callable(args, 1, "arr.find");
            foreach (StashValue item in list)
            {
                if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
                {
                    return item;
                }
            }
            return StashValue.Null;
        });

        ns.Function("reduce", [Param("array", "array"), Param("fn", "function"), Param("initial")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.reduce");
            var fn = SvArgs.Callable(args, 1, "arr.reduce");
            StashValue accumulator = args[2];
            foreach (StashValue item in list)
            {
                accumulator = ctx.InvokeCallbackDirect(fn, new StashValue[] { accumulator, item });
            }
            return accumulator;
        });

        ns.Function("unique", [Param("array", "array"), Param("fn?", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'arr.unique' requires 1 or 2 arguments.");
            var list = SvArgs.StashList(args, 0, "arr.unique");
            var result = new List<StashValue>();
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
            return StashValue.FromObj(result);
        },
            isVariadic: true,
            documentation: "Returns a new array with duplicate values removed. When fn is provided, uses fn(element) as the uniqueness key — elements with the same key are considered duplicates; the first occurrence is kept.");

        ns.Function("any", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.any");
            var fn = SvArgs.Callable(args, 1, "arr.any");
            foreach (StashValue item in list)
            {
                if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
                {
                    return StashValue.True;
                }
            }
            return StashValue.False;
        });

        ns.Function("every", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.every");
            var fn = SvArgs.Callable(args, 1, "arr.every");
            foreach (StashValue item in list)
            {
                if (!RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
                {
                    return StashValue.False;
                }
            }
            return StashValue.True;
        });

        ns.Function("flat", [Param("array", "array"), Param("depth?", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'arr.flat' requires 1 or 2 arguments.");
            var list = SvArgs.StashList(args, 0, "arr.flat");
            int depth = args.Length == 2 ? (int)SvArgs.Long(args, 1, "arr.flat") : 1;
            var result = new List<StashValue>();
            FlattenInto(list, result, depth);
            return StashValue.FromObj(result);
        },
            isVariadic: true,
            documentation: "Returns a new array with nested arrays flattened. Optional depth (default 1) controls how many levels to flatten; use -1 to flatten completely.");

        ns.Function("flatMap", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.flatMap");
            var fn = SvArgs.Callable(args, 1, "arr.flatMap");
            var result = new List<StashValue>();
            foreach (StashValue item in list)
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
            return StashValue.FromObj(result);
        });

        ns.Function("findIndex", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.findIndex");
            var fn = SvArgs.Callable(args, 1, "arr.findIndex");
            for (int i = 0; i < list.Count; i++)
            {
                if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { list[i] }).ToObject()))
                {
                    return StashValue.FromInt(i);
                }
            }
            return StashValue.FromInt(-1);
        });

        ns.Function("count", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.count");
            var fn = SvArgs.Callable(args, 1, "arr.count");
            long count = 0;
            foreach (StashValue item in list)
            {
                if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject()))
                {
                    count++;
                }
            }
            return StashValue.FromInt(count);
        });

        ns.Function("sortBy", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.sortBy");
            var fn = SvArgs.Callable(args, 1, "arr.sortBy");
            var sorted = new List<StashValue>(list);
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
            return StashValue.FromObj(sorted);
        });

        ns.Function("groupBy", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.groupBy");
            var fn = SvArgs.Callable(args, 1, "arr.groupBy");
            var result = new StashDictionary();
            foreach (StashValue item in list)
            {
                var key = RuntimeValues.Stringify(ctx.InvokeCallbackDirect(fn, new StashValue[] { item }).ToObject());
                var existing = result.Get(key);
                if (existing.AsObj is List<StashValue> svGroup)
                {
                    svGroup.Add(item);
                }
                else
                {
                    result.Set(key, StashValue.FromObj(new List<StashValue> { item }));
                }
            }
            return StashValue.FromObj(result);
        });

        ns.Function("sum", [Param("array", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.sum");
            long longTotal = 0;
            double doubleTotal = 0;
            bool hasFloat = false;
            foreach (var item in list)
            {
                var v = item.ToObject();
                if (v is long l) longTotal += l;
                else if (v is double d) { doubleTotal += d; hasFloat = true; }
                else throw new RuntimeError("'arr.sum' requires all elements to be numbers.");
            }
            return hasFloat ? StashValue.FromFloat(doubleTotal + longTotal) : StashValue.FromInt(longTotal);
        });

        ns.Function("min", [Param("array", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.min");
            if (list.Count == 0)
                throw new RuntimeError("'arr.min' requires a non-empty array.");
            double min = double.MaxValue;
            bool hasFloat = false;
            foreach (var item in list)
            {
                var v = item.ToObject();
                if (v is long l) { if (l < min) min = l; }
                else if (v is double d) { if (d < min) min = d; hasFloat = true; }
                else throw new RuntimeError("'arr.min' requires all elements to be numbers.");
            }
            return hasFloat ? StashValue.FromFloat(min) : StashValue.FromInt((long)min);
        });

        ns.Function("max", [Param("array", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.max");
            if (list.Count == 0)
                throw new RuntimeError("'arr.max' requires a non-empty array.");
            double max = double.MinValue;
            bool hasFloat = false;
            foreach (var item in list)
            {
                var v = item.ToObject();
                if (v is long l) { if (l > max) max = l; }
                else if (v is double d) { if (d > max) max = d; hasFloat = true; }
                else throw new RuntimeError("'arr.max' requires all elements to be numbers.");
            }
            return hasFloat ? StashValue.FromFloat(max) : StashValue.FromInt((long)max);
        });

        // ── Additional array utilities ───────────────────────────────────
        ns.Function("zip", [Param("a", "array"), Param("b", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var a = SvArgs.StashList(args, 0, "arr.zip");
            var b = SvArgs.StashList(args, 1, "arr.zip");
            int len = Math.Min(a.Count, b.Count);
            var result = new List<StashValue>(len);
            for (int i = 0; i < len; i++)
                result.Add(StashValue.FromObj(new List<StashValue> { a[i], b[i] }));
            return StashValue.FromObj(result);
        });

        ns.Function("chunk", [Param("array", "array"), Param("size", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.chunk");
            var size = SvArgs.Long(args, 1, "arr.chunk");
            if (size <= 0)
                throw new RuntimeError("'arr.chunk' size must be > 0.");
            var result = new List<StashValue>();
            for (int i = 0; i < list.Count; i += (int)size)
            {
                int chunkSize = Math.Min((int)size, list.Count - i);
                result.Add(StashValue.FromObj(list.GetRange(i, chunkSize)));
            }
            return StashValue.FromObj(result);
        });

        ns.Function("shuffle", [Param("array", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.shuffle");
            var rng = new Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return StashValue.Null;
        });

        ns.Function("take", [Param("array", "array"), Param("n", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.take");
            var n = SvArgs.Long(args, 1, "arr.take");
            int count = (int)Math.Max(0, Math.Min(n, list.Count));
            return StashValue.FromObj(list.GetRange(0, count));
        });

        ns.Function("drop", [Param("array", "array"), Param("n", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.drop");
            var n = SvArgs.Long(args, 1, "arr.drop");
            int skip = (int)Math.Max(0, Math.Min(n, list.Count));
            return StashValue.FromObj(list.GetRange(skip, list.Count - skip));
        });

        ns.Function("partition", [Param("array", "array"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var list = SvArgs.StashList(args, 0, "arr.partition");
            var fn = SvArgs.Callable(args, 1, "arr.partition");
            var matching = new List<StashValue>();
            var nonMatching = new List<StashValue>();
            foreach (StashValue item in list)
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
            return StashValue.FromObj(new List<StashValue> { StashValue.FromObj(matching), StashValue.FromObj(nonMatching) });
        });

        return ns.Build();
    }

    private static object? ParMap(IInterpreterContext ctx, List<StashValue> args)
    {
        if (args.Count < 2 || args[1].ToObject() is not IStashCallable callable)
        {
            throw new RuntimeError("arr.parMap() expects (array, function, [maxConcurrency]).");
        }
        List<StashValue> list;
        if (args[0].ToObject() is List<StashValue> l1) list = l1;
        else throw new RuntimeError("arr.parMap() expects (array, function, [maxConcurrency]).");

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

        return new List<StashValue>(results);
    }

    private static object? ParFilter(IInterpreterContext ctx, List<StashValue> args)
    {
        if (args.Count < 2 || args[1].ToObject() is not IStashCallable callable)
        {
            throw new RuntimeError("arr.parFilter() expects (array, function, [maxConcurrency]).");
        }
        List<StashValue> list;
        if (args[0].ToObject() is List<StashValue> l1) list = l1;
        else throw new RuntimeError("arr.parFilter() expects (array, function, [maxConcurrency]).");

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

        var filtered = new List<StashValue>();
        for (int i = 0; i < count; i++)
        {
            if (keep[i])
            {
                filtered.Add(list[i]);
            }
        }
        return filtered;
    }

    private static object? ParForEach(IInterpreterContext ctx, List<StashValue> args)
    {
        if (args.Count < 2 || args[1].ToObject() is not IStashCallable callable)
        {
            throw new RuntimeError("arr.parForEach() expects (array, function, [maxConcurrency]).");
        }
        List<StashValue> list;
        if (args[0].ToObject() is List<StashValue> l1) list = l1;
        else throw new RuntimeError("arr.parForEach() expects (array, function, [maxConcurrency]).");

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
                throw new RuntimeError($"Third argument to '{fnName}' (maxConcurrency) must be >= 1.");
            }

            return (int)n;
        }

        throw new RuntimeError($"Third argument to '{fnName}' (maxConcurrency) must be an integer.");
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

        throw new RuntimeError("Cannot compare values of incompatible types in 'arr.sortBy'.");
    }
}
