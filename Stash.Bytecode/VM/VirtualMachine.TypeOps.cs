using System;
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
    private static readonly HashSet<string> _knownTypeNames = new(StringComparer.Ordinal)
    {
        "int", "float", "string", "bool", "array", "dict", "null", "function",
        "range", "duration", "bytes", "semver", "secret", "ip", "Error", "struct", "enum",
        "interface", "namespace", "Future"
    };

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
        "secret"    => value is StashSecret,
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

    private void ExecuteIs(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte rawC = Instruction.GetC(inst);
        bool isDynamic = (rawC & 0x80) != 0;
        byte c = (byte)(rawC & 0x7F);
        int @base = frame.BaseSlot;
        SourceSpan? span = GetCurrentSpan(ref frame);

        object? value = _stack[@base + b].ToObject();
        object? typeObj = _stack[@base + c].ToObject();
        bool result;

        if (typeObj is string typeName)
        {
            // String type name: check globals first for user-defined struct/enum/interface.
            Dictionary<string, StashValue> globals = frame.ModuleGlobals ?? _globals;
            if (globals.TryGetValue(typeName, out StashValue globalSv) &&
                globalSv.AsObj is StashStruct or StashEnum or StashInterface)
            {
                object? globalType = globalSv.AsObj;
                result = globalType switch
                {
                    StashStruct sd2    => value is StashInstance inst2 && inst2.TypeName == sd2.Name,
                    StashEnum se2      => value is StashEnumValue ev2 && ev2.TypeName == se2.Name,
                    StashInterface si2 => value is StashInstance inst3 &&
                        InstanceImplementsInterfaceName(inst3, si2.Name),
                    _ => false,
                };
            }
            else if (_knownTypeNames.Contains(typeName))
            {
                result = CheckIsType(value, typeName);
            }
            else if (value is StashEnumValue ev && ev.TypeName == typeName)
            {
                result = true;
            }
            else if (value is StashInstance inst2 && inst2.TypeName == typeName)
            {
                result = true;
            }
            else if (isDynamic)
            {
                throw new RuntimeError(
                    $"Right-hand side of 'is' must be a type, got '{typeName}'.",
                    span);
            }
            else
            {
                result = false;
            }
        }
        else
        {
            result = typeObj switch
            {
                StashStruct sd    => value is StashInstance inst4 && inst4.TypeName == sd.Name,
                StashEnum se      => value is StashEnumValue ev && ev.TypeName == se.Name,
                StashInterface si => value is StashInstance inst5 &&
                    InstanceImplementsInterfaceName(inst5, si.Name),
                _ => throw new RuntimeError(
                    $"Right-hand side of 'is' must be a type, got {RuntimeValues.Stringify(typeObj)}.",
                    span),
            };
        }

        _stack[@base + a] = StashValue.FromBool(result);
    }

    private void ExecuteStructDecl(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        ushort metaIdx = Instruction.GetBx(inst);
        int @base = frame.BaseSlot;
        SourceSpan? span = GetCurrentSpan(ref frame);
        var metadata = (StructMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

        // Method closures are in R(A+1)..R(A+N) in declaration order
        var methods = new Dictionary<string, IStashCallable>(metadata.MethodNames.Length);
        for (int i = 0; i < metadata.MethodNames.Length; i++)
        {
            object? methodObj = _stack[@base + a + 1 + i].ToObject();
            if (methodObj is VMFunction vmFunc)
                methods[metadata.MethodNames[i]] = vmFunc;
            else
                throw new RuntimeError($"Expected function for method '{metadata.MethodNames[i]}'.", span);
        }

        var fieldList = new List<string>(metadata.Fields);
        var structDef = new StashStruct(metadata.Name, fieldList, methods);

        // Resolve and validate interfaces
        Dictionary<string, StashValue> globals = frame.ModuleGlobals ?? _globals;
        foreach (string ifaceName in metadata.InterfaceNames)
        {
            if (!globals.TryGetValue(ifaceName, out StashValue resolvedSv) ||
                resolvedSv.AsObj is not StashInterface iface)
            {
                throw new RuntimeError($"'{ifaceName}' is not an interface.", span);
            }

            foreach (InterfaceField reqField in iface.RequiredFields)
            {
                if (!fieldList.Contains(reqField.Name))
                    throw new RuntimeError(
                        $"Struct '{metadata.Name}' does not implement interface '{ifaceName}': missing field '{reqField.Name}'.",
                        span);
            }

            foreach (InterfaceMethod reqMethod in iface.RequiredMethods)
            {
                if (!methods.ContainsKey(reqMethod.Name))
                    throw new RuntimeError(
                        $"Struct '{metadata.Name}' does not implement interface '{ifaceName}': missing method '{reqMethod.Name}'.",
                        span);

                int reqUserArity = reqMethod.ParameterNames.Contains("self") ? reqMethod.Arity - 1 : reqMethod.Arity;
                if (methods[reqMethod.Name] is VMFunction vmMethod)
                {
                    int implUserArity = vmMethod.Chunk.Arity - 1;
                    if (implUserArity != reqUserArity)
                        throw new RuntimeError(
                            $"Struct '{metadata.Name}' implements interface '{ifaceName}': method '{reqMethod.Name}' has wrong number of parameters (expected {reqUserArity}, got {implUserArity}).",
                            span);
                }
            }

            structDef.Interfaces.Add(iface);
        }

        _stack[@base + a] = StashValue.FromObj(structDef);
    }

    private void ExecuteEnumDecl(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        ushort metaIdx = Instruction.GetBx(inst);
        int @base = frame.BaseSlot;
        var metadata = (EnumMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

        var members = new List<string>(metadata.Members);
        var enumDef = new StashEnum(metadata.Name, members);
        _stack[@base + a] = StashValue.FromObj(enumDef);
    }

    private void ExecuteIfaceDecl(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        ushort metaIdx = Instruction.GetBx(inst);
        int @base = frame.BaseSlot;
        var metadata = (InterfaceMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

        var requiredFields = new List<InterfaceField>(metadata.Fields);
        var requiredMethods = new List<InterfaceMethod>(metadata.Methods);
        var interfaceDef = new StashInterface(metadata.Name, requiredFields, requiredMethods);
        _stack[@base + a] = StashValue.FromObj(interfaceDef);
    }

    private void ExecuteExtend(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        ushort metaIdx = Instruction.GetBx(inst);
        int @base = frame.BaseSlot;
        SourceSpan? span = GetCurrentSpan(ref frame);
        var metadata = (ExtendMetadata)frame.Chunk.Constants[metaIdx].AsObj!;

        // Method closures in R(A+1)..R(A+N) (baseReg=R(A) is an unused placeholder)
        var methodFuncs = new IStashCallable[metadata.MethodNames.Length];
        for (int i = 0; i < metadata.MethodNames.Length; i++)
        {
            object? methodObj = _stack[@base + a + 1 + i].ToObject();
            if (methodObj is not VMFunction vmFunc)
                throw new RuntimeError($"Expected function for extension method '{metadata.MethodNames[i]}'.", span);
            methodFuncs[i] = vmFunc;
        }

        if (metadata.IsBuiltIn)
        {
            for (int i = 0; i < metadata.MethodNames.Length; i++)
                _extensionRegistry.Register(metadata.TypeName, metadata.MethodNames[i], methodFuncs[i]);
        }
        else
        {
            Dictionary<string, StashValue> globals = frame.ModuleGlobals ?? _globals;
            if (!globals.TryGetValue(metadata.TypeName, out StashValue resolvedSv) ||
                resolvedSv.AsObj is not StashStruct structDef)
            {
                throw new RuntimeError($"Cannot extend '{metadata.TypeName}': not a known type.", span);
            }

            for (int i = 0; i < metadata.MethodNames.Length; i++)
            {
                string methodName = metadata.MethodNames[i];
                if (!structDef.OriginalMethodNames.Contains(methodName))
                    structDef.Methods[methodName] = methodFuncs[i];
            }
        }
    }

    private void ExecuteNewStruct(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte fieldCount = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        SourceSpan? span = GetCurrentSpan(ref frame);
        var meta = (StructInitMetadata)frame.Chunk.Constants[b].AsObj!;

        // Layout:
        //   !HasTypeReg: R(A)=dest, R(A+1)..R(A+fieldCount) = field values
        //    HasTypeReg: R(A)=dest, R(A+1)=type ref, R(A+2)..R(A+1+fieldCount) = field values
        StashStruct structDef;
        int fieldOffset;
        if (meta.HasTypeReg)
        {
            object? typeRef = _stack[@base + a + 1].ToObject();
            structDef = typeRef as StashStruct
                ?? throw new RuntimeError(
                    $"Struct literal: expected a struct type, got {RuntimeValues.Stringify(typeRef)}.", span);
            fieldOffset = a + 2;
        }
        else
        {
            Dictionary<string, StashValue> globals = frame.ModuleGlobals ?? _globals;
            if (!globals.TryGetValue(meta.TypeName, out StashValue sv) || sv.AsObj is not StashStruct sd)
                throw new RuntimeError($"Undefined struct type '{meta.TypeName}'.", span);
            structDef = sd;
            fieldOffset = a + 1;
        }

        var fieldSlots = new StashValue[structDef.Fields.Count];
        for (int i = 0; i < fieldCount; i++)
        {
            string fieldName = meta.FieldNames[i];

            if (!structDef.FieldIndices.TryGetValue(fieldName, out int slotIdx))
                throw new RuntimeError($"Unknown field '{fieldName}' for struct '{structDef.Name}'.", span);

            // Duplicate check: scan previous field names
            for (int j = 0; j < i; j++)
            {
                if (meta.FieldNames[j] == fieldName)
                    throw new RuntimeError($"Duplicate field '{fieldName}' in struct literal.", span);
            }

            fieldSlots[slotIdx] = _stack[@base + fieldOffset + i];
        }

        _stack[@base + a] = StashValue.FromObj(new StashInstance(structDef.Name, structDef, fieldSlots));
    }

    private void ExecuteTypeOf(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        int @base = frame.BaseSlot;
        object? value = _stack[@base + b].ToObject();
        string typeName = value switch
        {
            null                => "null",
            bool                => "bool",
            long                => "int",
            double              => "float",
            string              => "string",
            List<StashValue>    => "array",
            StashDictionary     => "dict",
            StashRange          => "range",
            StashDuration       => "duration",
            StashByteSize       => "bytes",
            StashSemVer         => "semver",
            StashSecret         => "secret",
            StashIpAddress      => "ip",
            StashError          => "Error",
            StashInstance si    => si.TypeName,
            StashEnumValue ev   => ev.TypeName,
            StashStruct         => "struct",
            StashEnum           => "enum",
            StashInterface      => "interface",
            StashNamespace      => "namespace",
            StashFuture         => "Future",
            VMFunction          => "function",
            IStashCallable      => "function",
            _                   => "unknown",
        };
        _stack[@base + a] = StashValue.FromObj(typeName);
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
