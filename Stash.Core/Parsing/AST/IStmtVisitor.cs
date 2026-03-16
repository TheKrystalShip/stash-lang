namespace Stash.Parsing.AST;

/// <summary>
/// Defines the visitor interface for traversing the Stash statement AST.
/// </summary>
public interface IStmtVisitor<T>
{
    T VisitExprStmt(ExprStmt stmt);
    T VisitVarDeclStmt(VarDeclStmt stmt);
    T VisitConstDeclStmt(ConstDeclStmt stmt);
    T VisitBlockStmt(BlockStmt stmt);
    T VisitIfStmt(IfStmt stmt);
    T VisitWhileStmt(WhileStmt stmt);
    T VisitDoWhileStmt(DoWhileStmt stmt);
    T VisitForInStmt(ForInStmt stmt);
    T VisitBreakStmt(BreakStmt stmt);
    T VisitContinueStmt(ContinueStmt stmt);
    T VisitFnDeclStmt(FnDeclStmt stmt);
    T VisitReturnStmt(ReturnStmt stmt);
    T VisitStructDeclStmt(StructDeclStmt stmt);
    T VisitEnumDeclStmt(EnumDeclStmt stmt);
    T VisitImportStmt(ImportStmt stmt);
    T VisitImportAsStmt(ImportAsStmt stmt);
    T VisitDestructureStmt(DestructureStmt stmt);
}
