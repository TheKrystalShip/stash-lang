using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Parsing.AST;
using Stash.Interpreting.Types;
using Stash.Interpreting.Exceptions;

namespace Stash.Interpreting;

public partial class Interpreter
{
    /// <inheritdoc />
    public object? VisitExprStmt(ExprStmt stmt)
    {
        stmt.Expression.Accept(this);
        return null;
    }

    /// <inheritdoc />
    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        object? value = null;
        if (stmt.Initializer is not null)
        {
            value = stmt.Initializer.Accept(this);
        }

        _environment.Define(stmt.Name.Lexeme, value);
        return null;
    }

    /// <inheritdoc />
    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        object? value = stmt.Initializer.Accept(this);
        _environment.DefineConstant(stmt.Name.Lexeme, value);
        return null;
    }

    /// <inheritdoc />
    public object? VisitDestructureStmt(DestructureStmt stmt)
    {
        object? value = stmt.Initializer.Accept(this);

        if (stmt.Kind == DestructureStmt.PatternKind.Array)
        {
            if (value is not List<object?> list)
            {
                throw new RuntimeError("Cannot destructure non-array value with array pattern.", stmt.Initializer.Span);
            }

            for (int i = 0; i < stmt.Names.Count; i++)
            {
                object? element = i < list.Count ? list[i] : null;
                if (stmt.IsConst)
                {
                    _environment.DefineConstant(stmt.Names[i].Lexeme, element);
                }
                else
                {
                    _environment.Define(stmt.Names[i].Lexeme, element);
                }
            }
        }
        else // Object pattern
        {
            for (int i = 0; i < stmt.Names.Count; i++)
            {
                string fieldName = stmt.Names[i].Lexeme;
                object? fieldValue;

                if (value is StashInstance instance)
                {
                    fieldValue = instance.GetField(fieldName, stmt.Names[i].Span);
                }
                else if (value is StashDictionary dict)
                {
                    fieldValue = dict.Get(fieldName);
                }
                else
                {
                    throw new RuntimeError("Cannot destructure value with object pattern. Expected a struct instance or dictionary.", stmt.Initializer.Span);
                }

                if (stmt.IsConst)
                {
                    _environment.DefineConstant(fieldName, fieldValue);
                }
                else
                {
                    _environment.Define(fieldName, fieldValue);
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitBlockStmt(BlockStmt stmt)
    {
        ExecuteBlock(stmt.Statements, new Environment(_environment));
        return null;
    }

    /// <inheritdoc />
    public object? VisitIfStmt(IfStmt stmt)
    {
        if (IsTruthy(stmt.Condition.Accept(this)))
        {
            Execute(stmt.ThenBranch);
        }
        else if (stmt.ElseBranch is not null)
        {
            Execute(stmt.ElseBranch);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitWhileStmt(WhileStmt stmt)
    {
        while (EvalConditionTruthy(stmt.Condition))
        {
            try
            {
                Execute(stmt.Body);
            }
            catch (BreakException)
            {
                break;
            }
            catch (ContinueException)
            {
                // Continue to next loop iteration
            }
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitDoWhileStmt(DoWhileStmt stmt)
    {
        do
        {
            try
            {
                Execute(stmt.Body);
            }
            catch (BreakException)
            {
                break;
            }
            catch (ContinueException)
            {
                // Continue to condition check
            }
        } while (EvalConditionTruthy(stmt.Condition));

        return null;
    }

    /// <inheritdoc />
    public object? VisitForInStmt(ForInStmt stmt)
    {
        object? iterable = stmt.Iterable.Accept(this);

        // Special case: two-variable for-in on dictionary iterates key-value pairs
        if (iterable is StashDictionary dict && stmt.IndexName is not null)
        {
            foreach (var kvp in dict.RawEntries())
            {
                var loopEnv = new Environment(_environment);
                loopEnv.Define(stmt.IndexName.Lexeme, kvp.Key);
                loopEnv.Define(stmt.VariableName.Lexeme, kvp.Value);

                try
                {
                    ExecuteBlock(stmt.Body.Statements, loopEnv);
                }
                catch (BreakException)
                {
                    break;
                }
                catch (ContinueException)
                {
                    // Continue to next iteration
                }
            }

            return null;
        }

        IEnumerable<object?> items;
        if (iterable is List<object?> list)
        {
            items = new List<object?>(list);
        }
        else if (iterable is string str)
        {
            items = StringToChars(str);
        }
        else if (iterable is StashDictionary dict2)
        {
            items = dict2.IterableKeys().ToList();
        }
        else if (iterable is StashRange range)
        {
            items = range.Iterate();
        }
        else
        {
            throw new RuntimeError("Can only iterate over arrays, strings, dictionaries, and ranges.", stmt.Iterable.Span);
        }

        long idx = 0;
        foreach (object? item in items)
        {
            var loopEnv = new Environment(_environment);
            if (stmt.IndexName is not null)
            {
                loopEnv.Define(stmt.IndexName.Lexeme, idx);
            }
            loopEnv.Define(stmt.VariableName.Lexeme, item);

            try
            {
                ExecuteBlock(stmt.Body.Statements, loopEnv);
            }
            catch (BreakException)
            {
                break;
            }
            catch (ContinueException)
            {
                // Continue to next iteration
            }
            idx++;
        }

        return null;
    }

    /// <summary>Converts a string to a sequence of single-character strings for iteration.</summary>
    private static IEnumerable<object?> StringToChars(string str) => RuntimeValues.StringToChars(str);

    /// <inheritdoc />
    public object? VisitBreakStmt(BreakStmt stmt)
    {
        throw new BreakException();
    }

    /// <inheritdoc />
    public object? VisitContinueStmt(ContinueStmt stmt)
    {
        throw new ContinueException();
    }

    /// <inheritdoc />
    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        object? value = null;
        if (stmt.Value is not null)
        {
            value = stmt.Value.Accept(this);
        }

        throw new ReturnException(value);
    }

    /// <inheritdoc />
    public object? VisitThrowStmt(ThrowStmt stmt)
    {
        object? value = stmt.Value.Accept(this);

        if (value is string message)
        {
            throw new RuntimeError(message, stmt.Span);
        }

        if (value is StashDictionary dict)
        {
            string msg = dict.Get("message") as string ?? "Unknown error";
            string type = dict.Get("type") as string ?? "Error";
            throw new RuntimeError(msg, stmt.Span, type);
        }

        if (value is StashError error)
        {
            throw new RuntimeError(error.Message, stmt.Span, error.Type);
        }

        throw new RuntimeError(RuntimeValues.Stringify(value), stmt.Span);
    }

    /// <inheritdoc />
    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        var function = new StashFunction(stmt, _environment);
        _environment.Define(stmt.Name.Lexeme, function);
        return null;
    }

    /// <inheritdoc />
    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        var fields = new List<string>();
        foreach (var field in stmt.Fields)
        {
            fields.Add(field.Lexeme);
        }

        var methods = new Dictionary<string, StashFunction>();
        foreach (var method in stmt.Methods)
        {
            methods[method.Name.Lexeme] = new StashFunction(method, _environment);
        }

        var structDef = new StashStruct(stmt.Name.Lexeme, fields, methods);

        // Resolve and validate interface conformance
        foreach (var ifaceToken in stmt.Interfaces)
        {
            object? resolved = _environment.Get(ifaceToken.Lexeme, ifaceToken.Span);
            if (resolved is not StashInterface iface)
            {
                throw new RuntimeError($"'{ifaceToken.Lexeme}' is not an interface.", ifaceToken.Span);
            }

            if (!structDef.Interfaces.Contains(iface))
            {
                ValidateInterfaceConformance(structDef, iface, stmt.Span);
                structDef.Interfaces.Add(iface);
            }
        }

        _environment.Define(stmt.Name.Lexeme, structDef);
        return null;
    }

    /// <summary>
    /// Validates that a struct satisfies all requirements of an interface.
    /// </summary>
    private static void ValidateInterfaceConformance(StashStruct @struct, StashInterface iface, SourceSpan span)
    {
        ValidateRequiredFields(@struct, iface, span);
        ValidateRequiredMethods(@struct, iface, span);
    }

    private static void ValidateRequiredFields(StashStruct @struct, StashInterface iface, SourceSpan span)
    {
        foreach (var field in iface.RequiredFields)
        {
            if (!@struct.Fields.Contains(field.Name))
            {
                throw new RuntimeError(
                    $"Struct '{@struct.Name}' does not implement field '{field.Name}' required by interface '{iface.Name}'.",
                    span);
            }
        }
    }

    private static void ValidateRequiredMethods(StashStruct @struct, StashInterface iface, SourceSpan span)
    {
        foreach (var method in iface.RequiredMethods)
        {
            if (!@struct.Methods.TryGetValue(method.Name, out StashFunction? structMethod))
            {
                throw new RuntimeError(
                    $"Struct '{@struct.Name}' does not implement method '{method.Name}' required by interface '{iface.Name}'.",
                    span);
            }

            if (structMethod.Arity != method.Arity)
            {
                throw new RuntimeError(
                    $"Method '{method.Name}' in struct '{@struct.Name}' has {structMethod.Arity} parameter(s), but interface '{iface.Name}' requires {method.Arity}.",
                    span);
            }
        }
    }

    /// <inheritdoc />
    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        var members = new List<string>();
        foreach (var member in stmt.Members)
        {
            members.Add(member.Lexeme);
        }

        var enumDef = new StashEnum(stmt.Name.Lexeme, members);
        _environment.Define(stmt.Name.Lexeme, enumDef);
        return null;
    }

    /// <inheritdoc />
    public object? VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        var requiredFields = new List<InterfaceField>();
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            string fieldName = stmt.Fields[i].Lexeme;
            string? typeHint = i < stmt.FieldTypes.Count ? stmt.FieldTypes[i]?.Lexeme : null;
            requiredFields.Add(new InterfaceField(fieldName, typeHint));
        }

        var requiredMethods = new List<InterfaceMethod>();
        foreach (var method in stmt.Methods)
        {
            var paramNames = new List<string>();
            var paramTypes = new List<string?>();
            foreach (var p in method.Parameters)
            {
                paramNames.Add(p.Lexeme);
            }
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                string? paramType = i < method.ParameterTypes.Count ? method.ParameterTypes[i]?.Lexeme : null;
                paramTypes.Add(paramType);
            }
            string? returnType = method.ReturnType?.Lexeme;
            requiredMethods.Add(new InterfaceMethod(method.Name.Lexeme, method.Parameters.Count, paramNames, paramTypes, returnType));
        }

        var interfaceDef = new StashInterface(stmt.Name.Lexeme, requiredFields, requiredMethods);
        _environment.Define(stmt.Name.Lexeme, interfaceDef);
        return null;
    }
}
