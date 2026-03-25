namespace Stash.Lsp.Analysis;

internal static class SemanticTokenConstants
{
    // Token type indices (must match legend registration order in SemanticTokensHandler)
    internal const int TokenTypeNamespace = 0;
    internal const int TokenTypeType = 1;
    internal const int TokenTypeFunction = 2;
    internal const int TokenTypeParameter = 3;
    internal const int TokenTypeVariable = 4;
    internal const int TokenTypeProperty = 5;
    internal const int TokenTypeEnumMember = 6;
    internal const int TokenTypeKeyword = 7;
    internal const int TokenTypeNumber = 8;
    internal const int TokenTypeString = 9;
    internal const int TokenTypeComment = 10;
    internal const int TokenTypeOperator = 11;

    // Token modifier bit flags
    internal const int ModifierDeclaration = 1 << 0;
    internal const int ModifierReadonly = 1 << 1;
}
