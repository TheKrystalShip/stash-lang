namespace Stash.Analysis;

public static class SemanticTokenConstants
{
    // Token type indices (must match legend registration order in SemanticTokensHandler)
    public const int TokenTypeNamespace = 0;
    public const int TokenTypeType = 1;
    public const int TokenTypeStruct = 2;
    public const int TokenTypeEnum = 3;
    public const int TokenTypeInterface = 4;
    public const int TokenTypeFunction = 5;
    public const int TokenTypeMethod = 6;
    public const int TokenTypeParameter = 7;
    public const int TokenTypeVariable = 8;
    public const int TokenTypeProperty = 9;
    public const int TokenTypeEnumMember = 10;

    // Token modifier bit flags
    public const int ModifierDeclaration = 1 << 0;
    public const int ModifierReadonly = 1 << 1;
    public const int ModifierDefaultLibrary = 1 << 2;
    public const int ModifierAsync = 1 << 3;
}
