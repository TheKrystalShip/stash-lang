namespace Stash.Parsing.AST;

/// <summary>
/// Defines the visitor interface for traversing the Stash statement AST.
/// </summary>
/// <typeparam name="T">The return type produced by each visit method.</typeparam>
/// <remarks>
/// This is the statement half of the Visitor pattern used throughout the Stash AST.
/// By separating operations (interpretation, static analysis, formatting) from
/// node definitions, new passes can be added without modifying any <see cref="Stmt"/> subclass.
/// Each concrete <see cref="Stmt"/> node dispatches to the corresponding method via
/// <see cref="Stmt.Accept{T}"/>.
/// </remarks>
public interface IStmtVisitor<T>
{
    /// <summary>
    /// Visits an <see cref="ExprStmt"/> node (an expression used as a statement, e.g. a function call).
    /// </summary>
    T VisitExprStmt(ExprStmt stmt);

    /// <summary>
    /// Visits a <see cref="VarDeclStmt"/> node (variable declaration <c>let x = expr;</c>).
    /// </summary>
    T VisitVarDeclStmt(VarDeclStmt stmt);

    /// <summary>
    /// Visits a <see cref="ConstDeclStmt"/> node (constant declaration <c>const X = expr;</c>).
    /// </summary>
    T VisitConstDeclStmt(ConstDeclStmt stmt);

    /// <summary>
    /// Visits a <see cref="BlockStmt"/> node (a brace-delimited block <c>{ ... }</c>).
    /// </summary>
    T VisitBlockStmt(BlockStmt stmt);

    /// <summary>
    /// Visits an <see cref="IfStmt"/> node (<c>if (cond) { ... } else { ... }</c>).
    /// </summary>
    T VisitIfStmt(IfStmt stmt);

    /// <summary>
    /// Visits a <see cref="WhileStmt"/> node (<c>while (cond) { ... }</c>).
    /// </summary>
    T VisitWhileStmt(WhileStmt stmt);

    /// <summary>
    /// Visits a <see cref="DoWhileStmt"/> node (<c>do { ... } while (cond);</c>).
    /// </summary>
    T VisitDoWhileStmt(DoWhileStmt stmt);

    /// <summary>
    /// Visits a <see cref="ForInStmt"/> node (<c>for (let x in collection) { ... }</c>).
    /// </summary>
    T VisitForInStmt(ForInStmt stmt);

    /// <summary>
    /// Visits a <see cref="BreakStmt"/> node (<c>break;</c>).
    /// </summary>
    T VisitBreakStmt(BreakStmt stmt);

    /// <summary>
    /// Visits a <see cref="ContinueStmt"/> node (<c>continue;</c>).
    /// </summary>
    T VisitContinueStmt(ContinueStmt stmt);

    /// <summary>
    /// Visits a <see cref="FnDeclStmt"/> node (function declaration <c>fn name(params) { body }</c>).
    /// </summary>
    T VisitFnDeclStmt(FnDeclStmt stmt);

    /// <summary>
    /// Visits a <see cref="ReturnStmt"/> node (<c>return expr;</c> or <c>return;</c>).
    /// </summary>
    T VisitReturnStmt(ReturnStmt stmt);

    /// <summary>
    /// Visits a <see cref="StructDeclStmt"/> node (struct declaration <c>struct Name { ... }</c>).
    /// </summary>
    T VisitStructDeclStmt(StructDeclStmt stmt);

    /// <summary>
    /// Visits an <see cref="EnumDeclStmt"/> node (enum declaration <c>enum Name { ... }</c>).
    /// </summary>
    T VisitEnumDeclStmt(EnumDeclStmt stmt);

    /// <summary>
    /// Visits an <see cref="ImportStmt"/> node (<c>import { name } from "file.stash";</c>).
    /// </summary>
    T VisitImportStmt(ImportStmt stmt);

    /// <summary>
    /// Visits an <see cref="ImportAsStmt"/> node (<c>import "file.stash" as alias;</c>).
    /// </summary>
    T VisitImportAsStmt(ImportAsStmt stmt);

    /// <summary>
    /// Visits a <see cref="DestructureStmt"/> node (<c>let [a, b] = expr;</c> or <c>let { x, y } = expr;</c>).
    /// </summary>
    T VisitDestructureStmt(DestructureStmt stmt);
}
