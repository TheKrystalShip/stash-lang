namespace Stash.Analysis;

public static class SemanticTokenConstants
{
    // Token type indices (must match legend registration order in SemanticTokensHandler)
    public const int TokenTypeNamespace = 0;
    public const int TokenTypeType = 1;
    public const int TokenTypeFunction = 2;
    public const int TokenTypeParameter = 3;
    public const int TokenTypeVariable = 4;
    public const int TokenTypeProperty = 5;
    public const int TokenTypeEnumMember = 6;
    public const int TokenTypeKeyword = 7;
    public const int TokenTypeNumber = 8;
    public const int TokenTypeString = 9;
    public const int TokenTypeComment = 10;
    public const int TokenTypeOperator = 11;
    public const int TokenTypeInterface = 12;

    // Token modifier bit flags
    public const int ModifierDeclaration = 1 << 0;
    public const int ModifierReadonly = 1 << 1;
}
