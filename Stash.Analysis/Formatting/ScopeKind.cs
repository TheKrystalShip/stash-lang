namespace Stash.Analysis.Formatting;

internal enum ScopeKind
{
    TopLevel,
    FunctionBody,
    StructBody,
    EnumBody,
    InterfaceBody,
    ExtendBody,
    ControlFlowBody,
    LambdaBody,
    TryCatchBody,
    ElevateBody,
    SwitchCase,
}
