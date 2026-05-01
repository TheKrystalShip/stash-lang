using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;
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
        "interface", "namespace", "Future",
        "byte", "int[]", "float[]", "string[]", "bool[]", "byte[]"
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

    private bool CheckIsType(object? value, string typeName) => typeName switch
    {
        "int"       => value is long,
        "float"     => value is double,
        "string"    => value is string,
        "bool"      => value is bool,
        "byte"      => value is byte,
        "array"     => value is List<StashValue> or StashTypedArray,
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
        _ when typeName.EndsWith("[]") => value is StashTypedArray ta2 && ta2.ElementTypeName == typeName[..^2],
        _           => value is StashEnumValue ev ? ev.TypeName == typeName
                     : value is StashInstance inst ? inst.TypeName == typeName
                     : _registeredTypeChecks.TryGetValue(typeName, out var pred) && value is not null && pred(value),
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
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
                    StashStruct sd2    => (value is StashInstance inst2 && inst2.TypeName == sd2.Name) ||
                                          (value is StashError errIs && sd2.IsBuiltIn &&
                                           ErrorTypeRegistry.Matches(errIs.Type, sd2.Name)),
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
            else if (value is StashEnumValue ev && ev.TypeName == typeName
                && globals.TryGetValue(typeName, out var evDescVal) && evDescVal.AsObj is StashEnum)
            {
                result = true;
            }
            else if (value is StashInstance inst2 && inst2.TypeName == typeName
                && globals.TryGetValue(typeName, out var instDescVal) && instDescVal.AsObj is StashStruct)
            {
                result = true;
            }
            else if (_registeredTypeChecks.TryGetValue(typeName, out var pred))
            {
                result = value is not null && pred(value);
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
            // At global scope (BaseSlot == 0), struct/enum names are also stored in global slots.
            // Verify the type is still in globals so that `unset S` makes `v is S` return false.
            // Inside functions (BaseSlot > 0), local structs are not in globals — skip the check.
            Dictionary<string, StashValue> globals2 = frame.ModuleGlobals ?? _globals;
            bool atGlobalScope = frame.BaseSlot == 0;
            result = typeObj switch
            {
                StashStruct sd    => value is StashInstance inst4 && inst4.TypeName == sd.Name
                                     && (!atGlobalScope || globals2.ContainsKey(sd.Name)),
                StashEnum se      => value is StashEnumValue ev && ev.TypeName == se.Name
                                     && (!atGlobalScope || globals2.ContainsKey(se.Name)),
                StashInterface si => value is StashInstance inst5 &&
                    InstanceImplementsInterfaceName(inst5, si.Name),
                _ => throw new RuntimeError(
                    $"Right-hand side of 'is' must be a type, got {RuntimeValues.Stringify(typeObj)}.",
                    span),
            };
        }

        _stack[@base + a] = StashValue.FromBool(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
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

    [MethodImpl(MethodImplOptions.NoInlining)]
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

    [MethodImpl(MethodImplOptions.NoInlining)]
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

    [MethodImpl(MethodImplOptions.NoInlining)]
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteDefer(ref CallFrame frame, uint inst)
    {
        int @base = frame.BaseSlot;
        byte a = Instruction.GetA(inst);
        StashValue closure = _stack[@base + a];
        (frame.Defers ??= new List<StashValue>()).Add(closure);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
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

    [MethodImpl(MethodImplOptions.NoInlining)]
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
            IVMTyped typed      => typed.VMTypeName,
            StashInterface      => "interface",
            VMFunction          => "function",
            IStashCallable      => "function",
            _                   => ResolveRegisteredTypeName(value),
        };
        _stack[@base + a] = StashValue.FromObj(typeName);
    }

    private string ResolveRegisteredTypeName(object? value)
    {
        if (value is not null && _registeredTypeNames.TryGetValue(value.GetType(), out string? name))
            return name;
        return "unknown";
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

        // 3. Protocol dispatch for all other types that implement IVMFieldAccessible
        if (obj is IVMFieldAccessible accessible)
        {
            if (accessible.VMTryGetField(name, out StashValue fieldResult, span))
                return fieldResult.ToObject();
        }

        // 4. Built-in type .length properties
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
        if (obj is IVMFieldMutable mutable)
        {
            mutable.VMSetField(name, StashValue.FromObject(value), span);
            return;
        }
        throw new RuntimeError($"Cannot set field '{name}' on {RuntimeValues.Stringify(obj)}.", span);
    }

}
