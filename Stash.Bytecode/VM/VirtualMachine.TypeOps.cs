using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Type checking, field access, and declaration opcode handlers.
/// </summary>
public sealed partial class VirtualMachine
{
    private static bool InstanceImplementsInterfaceName(StashInstance inst, string ifaceName)
    {
        if (inst.Struct == null)
        {
            return false;
        }

        foreach (StashInterface iface in inst.Struct.Interfaces)
        {
            if (iface.Name == ifaceName)
            {
                return true;
            }
        }
        return false;
    }

    private static bool CheckIsType(object? value, string typeName) => typeName switch
    {
        "int"       => value is long,
        "float"     => value is double,
        "string"    => value is string,
        "bool"      => value is bool,
        "array"     => value is List<StashValue>,
        "dict"      => value is StashDictionary,
        "null"      => value is null,
        "function"  => value is VMFunction or IStashCallable,
        "range"     => value is StashRange,
        "duration"  => value is StashDuration,
        "bytes"     => value is StashByteSize,
        "semver"    => value is StashSemVer,
        "ip"        => value is StashIpAddress,
        "Error"     => value is StashError,
        "struct"    => value is StashInstance,
        "enum"      => value is StashEnumValue,
        "interface" => value is StashInterface,
        "namespace" => value is StashNamespace,
        "Future"    => value is StashFuture,
        _           => value is StashEnumValue ev ? ev.TypeName == typeName
                     : value is StashInstance inst && inst.TypeName == typeName,
    };

    private void ExecuteIs(ref CallFrame frame)
    {
        ushort typeIdx = ReadU16(ref frame);
        if (typeIdx == 0xFFFF)
        {
            // Dynamic type check: type expression is on the stack
            object? typeObj = Pop().ToObject();
            object? value = Pop().ToObject();
            bool result = typeObj switch
            {
                StashStruct sd => value is StashInstance inst && inst.TypeName == sd.Name,
                StashEnum se => value is StashEnumValue ev && ev.TypeName == se.Name,
                StashInterface si => value is StashInstance inst2 &&
                    InstanceImplementsInterfaceName(inst2, si.Name),
                _ => throw new RuntimeError(
                    $"Right-hand side of 'is' must be a type, got {RuntimeValues.Stringify(typeObj)}.",
                    GetCurrentSpan(ref frame)),
            };
            Push(StashValue.FromBool(result));
        }
        else
        {
            string typeName = (string)frame.Chunk.Constants[typeIdx].AsObj!;
            object? value = Pop().ToObject();
            // Check globals for a variable holding a type definition (e.g. `let t = Foo; x is t`)
            Dictionary<string, StashValue> globals = frame.ModuleGlobals ?? _globals;
            if (globals.TryGetValue(typeName, out StashValue globalSv) &&
                globalSv.AsObj is StashStruct or StashEnum or StashInterface)
            {
                object? globalType = globalSv.AsObj;
                bool r = globalType switch
                {
                    StashStruct sd => value is StashInstance inst && inst.TypeName == sd.Name,
                    StashEnum se => value is StashEnumValue ev && ev.TypeName == se.Name,
                    StashInterface si => value is StashInstance inst2 &&
                        InstanceImplementsInterfaceName(inst2, si.Name),
                    _ => false,
                };
                Push(StashValue.FromBool(r));
            }
            else
            {
                Push(StashValue.FromBool(CheckIsType(value, typeName)));
            }
        }
    }

    private void ExecuteStructDecl(ref CallFrame frame)
    {
        ushort metaIdx = ReadU16(ref frame);
        SourceSpan? span = GetCurrentSpan(ref frame);
        var metadata = (StructMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

        // Pop method closures from stack (pushed in order, so pop in reverse)
        var methods = new Dictionary<string, IStashCallable>(metadata.MethodNames.Length);
        for (int i = metadata.MethodNames.Length - 1; i >= 0; i--)
        {
            object? methodObj = Pop().ToObject();
            if (methodObj is VMFunction vmFunc)
            {
                methods[metadata.MethodNames[i]] = vmFunc;
            }
            else
            {
                throw new RuntimeError($"Expected function for method '{metadata.MethodNames[i]}'.", span);
            }
        }

        var fieldList = new List<string>(metadata.Fields);
        var structDef = new StashStruct(metadata.Name, fieldList, methods);

        // Resolve and validate interfaces
        Dictionary<string, StashValue> globals = frame.ModuleGlobals ?? _globals;
        foreach (string ifaceName in metadata.InterfaceNames)
        {
            if (!globals.TryGetValue(ifaceName, out StashValue resolvedSv) || resolvedSv.AsObj is not StashInterface iface)
            {
                throw new RuntimeError($"'{ifaceName}' is not an interface.", span);
            }

            foreach (InterfaceField reqField in iface.RequiredFields)
            {
                if (!fieldList.Contains(reqField.Name))
                {
                    throw new RuntimeError(
                        $"Struct '{metadata.Name}' does not implement interface '{ifaceName}': missing field '{reqField.Name}'.",
                        span);
                }
            }

            foreach (InterfaceMethod reqMethod in iface.RequiredMethods)
            {
                if (!methods.ContainsKey(reqMethod.Name))
                {
                    throw new RuntimeError(
                        $"Struct '{metadata.Name}' does not implement interface '{ifaceName}': missing method '{reqMethod.Name}'.",
                        span);
                }

                // Arity check: normalize both sides to exclude 'self'
                // Interface Arity = sig.Parameters.Count (includes 'self' if explicitly declared)
                // Struct method Chunk.Arity always includes synthetic 'self' as first param
                int reqUserArity = reqMethod.ParameterNames.Contains("self") ? reqMethod.Arity - 1 : reqMethod.Arity;
                if (methods[reqMethod.Name] is VMFunction vmMethod)
                {
                    int implUserArity = vmMethod.Chunk.Arity - 1;
                    if (implUserArity != reqUserArity)
                    {
                        throw new RuntimeError(
                            $"Struct '{metadata.Name}' implements interface '{ifaceName}': method '{reqMethod.Name}' has wrong number of parameters (expected {reqUserArity}, got {implUserArity}).",
                            span);
                    }
                }
            }

            structDef.Interfaces.Add(iface);
        }

        Push(StashValue.FromObj(structDef));
    }

    private void ExecuteEnumDecl(ref CallFrame frame)
    {
        ushort metaIdx = ReadU16(ref frame);
        var metadata = (EnumMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

        var members = new List<string>(metadata.Members);
        var enumDef = new StashEnum(metadata.Name, members);
        Push(StashValue.FromObj(enumDef));
    }

    private void ExecuteInterfaceDecl(ref CallFrame frame)
    {
        ushort metaIdx = ReadU16(ref frame);
        var metadata = (InterfaceMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

        var requiredFields = new List<InterfaceField>(metadata.Fields);
        var requiredMethods = new List<InterfaceMethod>(metadata.Methods);
        var interfaceDef = new StashInterface(metadata.Name, requiredFields, requiredMethods);
        Push(StashValue.FromObj(interfaceDef));
    }

    private void ExecuteExtend(ref CallFrame frame)
    {
        ushort metaIdx = ReadU16(ref frame);
        SourceSpan? span = GetCurrentSpan(ref frame);
        var metadata = (ExtendMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

        // Pop method closures (pushed in order, so pop in reverse)
        var methodFuncs = new IStashCallable[metadata.MethodNames.Length];
        for (int i = metadata.MethodNames.Length - 1; i >= 0; i--)
        {
            object? methodObj = Pop().ToObject();
            if (methodObj is not VMFunction vmFunc)
            {
                throw new RuntimeError($"Expected function for extension method '{metadata.MethodNames[i]}'.", span);
            }

            methodFuncs[i] = vmFunc;
        }

        if (metadata.IsBuiltIn)
        {
            for (int i = 0; i < metadata.MethodNames.Length; i++)
            {
                _extensionRegistry.Register(metadata.TypeName, metadata.MethodNames[i], methodFuncs[i]);
            }
        }
        else
        {
            Dictionary<string, StashValue> globals = frame.ModuleGlobals ?? _globals;
            if (!globals.TryGetValue(metadata.TypeName, out StashValue resolvedSv) || resolvedSv.AsObj is not StashStruct structDef)
            {
                throw new RuntimeError($"Cannot extend '{metadata.TypeName}': not a known type.", span);
            }

            for (int i = 0; i < metadata.MethodNames.Length; i++)
            {
                string methodName = metadata.MethodNames[i];
                if (!structDef.OriginalMethodNames.Contains(methodName))
                {
                    structDef.Methods[methodName] = methodFuncs[i];
                }
            }
        }
    }
    private object? GetFieldValue(object? obj, string name, SourceSpan? span)
    {
        // 1. StashInstance: field + method access
        if (obj is StashInstance instance)
        {
            StashValue resultSv = instance.GetField(name, span);
            object? result = resultSv.ToObject();
            // Intercept StashBoundMethod wrapping VMFunction → return VMBoundMethod instead
            if (result is StashBoundMethod bound && bound.Method is VMFunction vmFunc)
            {
                return new VMBoundMethod(bound.Instance, vmFunc);
            }

            return result;
        }

        // 2. StashDictionary: extension methods first, then key access
        if (obj is StashDictionary dict)
        {
            if (_extensionRegistry.TryGetMethod("dict", name, out IStashCallable? dictExtMethod) &&
                dictExtMethod is VMFunction dictExtFunc)
            {
                return new VMExtensionBoundMethod(obj, dictExtFunc);
            }

            return dict.Get(name).ToObject();
        }

        // 3. StashNamespace: member access
        if (obj is StashNamespace ns)
        {
            return ns.GetMember(name, span);
        }

        // 4. StashStruct: static method access
        if (obj is StashStruct structDef)
        {
            if (structDef.Methods.TryGetValue(name, out IStashCallable? method))
            {
                return method;
            }

            throw new RuntimeError($"Struct '{structDef.Name}' has no static member '{name}'.", span);
        }

        // 5. StashEnum: member access
        if (obj is StashEnum enumDef)
        {
            StashEnumValue? enumVal = enumDef.GetMember(name);
            if (enumVal == null)
            {
                throw new RuntimeError($"Enum '{enumDef.Name}' has no member '{name}'.", span);
            }

            return enumVal;
        }

        // 6. StashEnumValue: property access
        if (obj is StashEnumValue enumValue)
        {
            return name switch
            {
                "typeName" => enumValue.TypeName,
                "memberName" => enumValue.MemberName,
                _ => throw new RuntimeError($"Enum value has no property '{name}'.", span)
            };
        }

        // 7. StashError: property access
        if (obj is StashError error)
        {
            return name switch
            {
                "message" => error.Message,
                "type" => error.Type,
                "stack" => error.Stack is not null ? new List<StashValue>(error.Stack.Select(s => StashValue.FromObj(s))) : null,
                _ => error.Properties?.TryGetValue(name, out object? propVal) == true
                    ? propVal
                    : throw new RuntimeError($"Error has no property '{name}'.", span)
            };
        }

        // StashDuration: property access
        if (obj is StashDuration dur)
        {
            return name switch
            {
                "totalMs" => (object)dur.TotalMilliseconds,
                "totalSeconds" => (object)dur.TotalSeconds,
                "totalMinutes" => (object)dur.TotalMinutes,
                "totalHours" => (object)dur.TotalHours,
                "totalDays" => (object)dur.TotalDays,
                "milliseconds" => (object)dur.Milliseconds,
                "seconds" => (object)dur.Seconds,
                "minutes" => (object)dur.Minutes,
                "hours" => (object)dur.Hours,
                "days" => (object)dur.Days,
                _ => throw new RuntimeError($"Duration has no property '{name}'.", span)
            };
        }

        // StashByteSize: property access
        if (obj is StashByteSize bs)
        {
            return name switch
            {
                "bytes" => (object)bs.TotalBytes,
                "kb" => (object)bs.Kb,
                "mb" => (object)bs.Mb,
                "gb" => (object)bs.Gb,
                "tb" => (object)bs.Tb,
                _ => throw new RuntimeError($"ByteSize has no property '{name}'.", span)
            };
        }

        // StashSemVer: property access
        if (obj is StashSemVer sv)
        {
            return name switch
            {
                "major" => (object)sv.Major,
                "minor" => (object)sv.Minor,
                "patch" => (object)sv.Patch,
                "prerelease" => (object)(sv.Prerelease ?? ""),
                "build" => (object)(sv.BuildMetadata ?? ""),
                "isPrerelease" => (object)sv.IsPrerelease,
                _ => throw new RuntimeError($"SemVer has no property '{name}'.", span)
            };
        }

        // StashIpAddress: property access
        if (obj is StashIpAddress ip)
        {
            return name switch
            {
                "address" => (object)ip.Address.ToString(),
                "version" => (object)(long)ip.Version,
                "prefixLength" => ip.PrefixLength.HasValue ? (object)(long)ip.PrefixLength.Value : null,
                "isLoopback" => (object)ip.IsLoopback,
                "isPrivate" => (object)ip.IsPrivate,
                "isLinkLocal" => (object)ip.IsLinkLocal,
                "isIPv4" => (object)(ip.Version == 4),
                "isIPv6" => (object)(ip.Version == 6),
                _ => throw new RuntimeError($"IpAddress has no property '{name}'.", span)
            };
        }

        // 8. Built-in type .length properties
        if (obj is List<StashValue> svList && name == "length")
        {
            return (long)svList.Count;
        }

        if (obj is string s && name == "length")
        {
            return (long)s.Length;
        }

        // 9. Extension methods on built-in types
        string? extTypeName = obj switch
        {
            string => "string",
            List<StashValue> => "array",
            long => "int",
            double => "float",
            _ => null
        };

        if (extTypeName is not null &&
            _extensionRegistry.TryGetMethod(extTypeName, name, out IStashCallable? extMethod))
        {
            if (extMethod is VMFunction extVmFunc)
            {
                return new VMExtensionBoundMethod(obj, extVmFunc);
            }

            return new BuiltInBoundMethod(obj, extMethod);
        }

        // 10. UFCS: namespace functions as methods on strings/arrays
        string? ufcsNsName = obj switch
        {
            string => "str",
            List<StashValue> => "arr",
            _ => null
        };

        if (ufcsNsName is not null &&
            _globals.TryGetValue(ufcsNsName, out StashValue nsSv) &&
            nsSv.AsObj is StashNamespace ufcsNs &&
            ufcsNs.HasMember(name))
        {
            object? member = ufcsNs.GetMember(name, span);
            if (member is IStashCallable callable)
            {
                return new BuiltInBoundMethod(obj, callable);
            }
        }

        throw new RuntimeError($"Cannot access field '{name}' on {RuntimeValues.Stringify(obj)}.", span);
    }

    private static void SetFieldValue(object? obj, string name, object? value, SourceSpan? span)
    {
        if (obj is StashInstance instance)
        {
            instance.SetField(name, StashValue.FromObject(value), span);
            return;
        }
        if (obj is StashDictionary dict)
        {
            dict.Set(name, StashValue.FromObject(value));
            return;
        }
        throw new RuntimeError($"Cannot set field '{name}' on {RuntimeValues.Stringify(obj)}.", span);
    }

}
