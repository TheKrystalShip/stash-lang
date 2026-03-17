using System.Collections.Generic;
using System.Linq;
using Stash.Parsing.AST;
using Stash.Interpreting.Types;
using Stash.Interpreting.Exceptions;

namespace Stash.Interpreting;

public partial class Interpreter
{
    public object? VisitExprStmt(ExprStmt stmt)
    {
        stmt.Expression.Accept(this);
        return null;
    }

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

    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        object? value = stmt.Initializer.Accept(this);
        _environment.DefineConstant(stmt.Name.Lexeme, value);
        return null;
    }

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

    public object? VisitBlockStmt(BlockStmt stmt)
    {
        ExecuteBlock(stmt.Statements, new Environment(_environment));
        return null;
    }

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

    public object? VisitWhileStmt(WhileStmt stmt)
    {
        while (IsTruthy(stmt.Condition.Accept(this)))
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
        } while (IsTruthy(stmt.Condition.Accept(this)));

        return null;
    }

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
            items = list;
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

    private static IEnumerable<object?> StringToChars(string str) => RuntimeValues.StringToChars(str);

    public object? VisitBreakStmt(BreakStmt stmt)
    {
        throw new BreakException();
    }

    public object? VisitContinueStmt(ContinueStmt stmt)
    {
        throw new ContinueException();
    }

    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        object? value = null;
        if (stmt.Value is not null)
        {
            value = stmt.Value.Accept(this);
        }

        throw new ReturnException(value);
    }

    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        var function = new StashFunction(stmt, _environment);
        _environment.Define(stmt.Name.Lexeme, function);
        return null;
    }

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
        _environment.Define(stmt.Name.Lexeme, structDef);
        return null;
    }

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
}
