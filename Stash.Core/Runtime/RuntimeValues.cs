namespace Stash.Runtime;

using System.Collections.Generic;
using System.Text;
using Stash.Runtime.Types;

/// <summary>
/// Static utility methods for Stash runtime value operations:
/// truthiness testing, stringification, equality, and numeric helpers.
/// </summary>
public static class RuntimeValues
{
    /// <summary>
    /// Determines whether a runtime value is truthy according to Stash's truthiness rules.
    /// </summary>
    public static bool IsTruthy(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is StashError)
        {
            return false;
        }

        if (value is bool b)
        {
            return b;
        }

        if (value is long i)
        {
            return i != 0;
        }

        if (value is double d)
        {
            return d != 0.0;
        }

        if (value is string s)
        {
            return s.Length != 0;
        }

        return true;
    }

    /// <summary>
    /// Converts a runtime value to its Stash string representation.
    /// </summary>
    public static string Stringify(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is bool b)
        {
            return b ? "true" : "false";
        }

        if (value is double d)
        {
            return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (value is StashError error)
        {
            return error.ToString();
        }

        if (value is StashInstance instance)
        {
            return instance.ToString();
        }

        if (value is StashStruct structDef)
        {
            return structDef.ToString();
        }

        if (value is StashEnumValue enumVal)
        {
            return enumVal.ToString();
        }

        if (value is StashEnum enumType)
        {
            return enumType.ToString();
        }

        if (value is StashInterface iface)
        {
            return iface.ToString();
        }

        if (value is StashNamespace ns)
        {
            return ns.ToString();
        }

        if (value is List<object?> list)
        {
            var elements = new StringBuilder("[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                {
                    elements.Append(", ");
                }

                elements.Append(Stringify(list[i]));
            }
            elements.Append(']');
            return elements.ToString();
        }

        if (value is StashDictionary dict)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var key in dict.Keys())
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;
                sb.Append(Stringify(key));
                sb.Append(": ");
                sb.Append(Stringify(dict.Get(key!)));
            }
            sb.Append('}');
            return sb.ToString();
        }

        if (value is StashRange range)
        {
            return range.ToString();
        }

        if (value is StashFuture future)
        {
            return future.ToString();
        }

        if (value is StashDuration duration)
        {
            return duration.ToString();
        }

        if (value is StashByteSize byteSize)
        {
            return byteSize.ToString();
        }

        if (value is StashSemVer semVer)
        {
            return semVer.ToString();
        }

        if (value is StashIpAddress ipAddr)
        {
            return ipAddr.ToString();
        }

        return value.ToString()!;
    }

    /// <summary>
    /// Tests two runtime values for equality without type coercion.
    /// </summary>
    public static bool IsEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.GetType() != right.GetType())
        {
            return false;
        }

        return object.Equals(left, right);
    }

    /// <summary>
    /// Checks whether a runtime value is a numeric type (long or double).
    /// </summary>
    public static bool IsNumeric(object? value) => value is long or double;

    /// <summary>
    /// Converts a numeric value to double for type-promoted arithmetic.
    /// </summary>
    public static double ToDouble(object? value) => value is long i ? (double)i : (double)value!;

    /// <summary>
    /// Converts a string into an enumerable of single-character strings.
    /// </summary>
    public static IEnumerable<object?> StringToChars(string str)
    {
        foreach (char c in str)
        {
            yield return c.ToString();
        }
    }

    /// <summary>
    /// Implements string padding (padStart / padEnd).
    /// </summary>
    public static string PadString(string funcName, List<object?> args, bool padLeft)
    {
        if (args.Count < 2 || args.Count > 3)
        {
            throw new RuntimeError($"'{funcName}' requires 2 or 3 arguments.");
        }

        if (args[0] is not string s)
        {
            throw new RuntimeError($"First argument to '{funcName}' must be a string.");
        }

        if (args[1] is not long length)
        {
            throw new RuntimeError($"Second argument to '{funcName}' must be an integer.");
        }

        char fillChar = ' ';
        if (args.Count == 3)
        {
            if (args[2] is not string fill || fill.Length != 1)
            {
                throw new RuntimeError($"Third argument to '{funcName}' must be a single-character string.");
            }

            fillChar = fill[0];
        }
        return padLeft ? s.PadLeft((int)length, fillChar) : s.PadRight((int)length, fillChar);
    }

    /// <summary>
    /// Creates a CommandResult StashInstance.
    /// </summary>
    public static StashInstance CreateCommandResult(string stdout, string stderr, long exitCode)
    {
        return new StashInstance("CommandResult", new Dictionary<string, object?>
        {
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["exitCode"] = exitCode
        }) { StringifyField = "stdout" };
    }

    /// <summary>
    /// Creates a deep copy of a Stash runtime value. Immutable values (primitives, enums,
    /// ranges, namespaces) are returned as-is. Mutable values (lists, dictionaries, struct
    /// instances, functions/lambdas with closures) are recursively cloned.
    /// </summary>
    public static object? DeepCopy(object? value)
    {
        return value switch
        {
            // Immutable — return as-is
            null => null,
            bool => value,
            long => value,
            double => value,
            string => value,
            StashEnumValue => value,
            StashEnum => value,
            StashStruct => value,
            StashRange => value,
            StashIpAddress => value,        // Immutable value type
            StashSemVer => value,           // Immutable value type
            StashError => value,
            StashNamespace => value,        // Frozen after init
            BuiltInFunction => value,       // Stateless delegate wrapper

            // Mutable collections — deep clone
            List<object?> list => DeepCopyList(list),
            StashDictionary dict => DeepCopyDictionary(dict),
            StashInstance instance => DeepCopyInstance(instance),

            // Callables with closures — use IDeepCopyable interface
            IDeepCopyable copyable => copyable.DeepCopy(),

            // StashBoundMethod — deep copy instance + method
            StashBoundMethod bound => DeepCopyBoundMethod(bound),

            // Unknown — return as-is (safety fallback)
            _ => value,
        };
    }

    private static List<object?> DeepCopyList(List<object?> list)
    {
        var copy = new List<object?>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            copy.Add(DeepCopy(list[i]));
        }
        return copy;
    }

    private static StashDictionary DeepCopyDictionary(StashDictionary dict)
    {
        var copy = new StashDictionary();
        foreach (var (key, val) in dict.GetAllEntries())
        {
            copy.Set(key, DeepCopy(val));
        }
        return copy;
    }

    private static StashInstance DeepCopyInstance(StashInstance instance)
    {
        var fieldsCopy = new Dictionary<string, object?>();
        foreach (var (name, val) in instance.GetAllFields())
        {
            fieldsCopy[name] = DeepCopy(val);
        }
        return new StashInstance(instance.TypeName, fieldsCopy, instance.Struct);
    }

    private static StashBoundMethod DeepCopyBoundMethod(StashBoundMethod bound)
    {
        var copiedInstance = (StashInstance)DeepCopy(bound.Instance)!;
        IStashCallable copiedMethod = bound.Method is IDeepCopyable dc
            ? (IStashCallable)dc.DeepCopy()
            : bound.Method;
        return new StashBoundMethod(copiedInstance, copiedMethod);
    }
}
