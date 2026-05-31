namespace Stash.Stdlib.Generators;

using Microsoft.CodeAnalysis;

internal static class Diagnostics
{
    private const string Category = "StashStdlibGenerator";

    public static readonly DiagnosticDescriptor UnsupportedParameterType = new(
        id: "STASH_GEN001",
        title: "Unsupported parameter type on [StashFn] method",
        messageFormat: "Unsupported parameter type '{0}' on [StashFn] method '{1}'. Supported types: long, double, string, bool, byte, byte[], List<StashValue>, StashDictionary, StashValue, IStashCallable, params StashValue[].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedReturnType = new(
        id: "STASH_GEN002",
        title: "Unsupported return type on [StashFn] method",
        messageFormat: "Unsupported return type '{0}' on [StashFn] method '{1}'. Use 'StashValue' to return arbitrary values.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InterpreterContextPosition = new(
        id: "STASH_GEN003",
        title: "IInterpreterContext must be the first parameter",
        messageFormat: "'IInterpreterContext' parameter must be the first parameter of [StashFn] method '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateFunctionName = new(
        id: "STASH_GEN005",
        title: "Duplicate Stash function name in namespace",
        messageFormat: "Two [StashFn] methods in namespace '{0}' produce the same Stash name '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidConstField = new(
        id: "STASH_GEN006",
        title: "[StashConst] requires a const or static readonly field",
        messageFormat: "[StashConst] may only be applied to 'const' or 'static readonly' fields. Field '{0}' does not qualify.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConsecutiveUppercaseInName = new(
        id: "STASH_GEN007",
        title: "Stash function name has consecutive uppercase letters",
        messageFormat: "[StashFn] method '{0}' produces Stash name '{1}' containing two consecutive uppercase letters. Use PascalCase acronyms (e.g. 'UrlEncode' not 'URLEncode').",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NamespaceClassMustBePartialStatic = new(
        id: "STASH_GEN008",
        title: "[StashNamespace] class must be partial and static",
        messageFormat: "[StashNamespace] class '{0}' must be 'partial' and 'static'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InaccessibleStructOrEnum = new(
        id: "STASH_GEN009",
        title: "[StashStruct]/[StashEnum] type not accessible from generated Define()",
        messageFormat: "[StashStruct]/[StashEnum] type '{0}' is not accessible from the generated Define() method",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingSummaryDoc = new(
        id: "STASH_DOC001",
        title: "[StashFn] missing <summary> doc comment",
        messageFormat: "[StashFn] method '{0}' is missing an XML <summary> doc comment",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingParamDoc = new(
        id: "STASH_DOC002",
        title: "[StashFn] parameter missing <param> doc comment",
        messageFormat: "[StashFn] parameter '{0}' of method '{1}' is missing a <param> doc comment",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ThrowsMetadataMismatch = new(
        id: "STSG010",
        title: "Throws metadata mismatch",
        messageFormat: "Throws metadata for {0} differs between [StashFn(Throws=...)] and <exception> tags. Attribute: [{1}]. Doc: [{2}]. Union will be used.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OldDocTagForm = new(
        id: "STSG011",
        title: "<exception cref> uses old StashErrorTypes field form",
        messageFormat: "[StashFn] method '{0}' has <exception cref=\"StashErrorTypes.X\"> tags. Use bare class names instead: <exception cref=\"IOError\"> rather than <exception cref=\"StashErrorTypes.IOError\">.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ThrowsTypesNotStashError = new(
        id: "STSG013",
        title: "[StashFn(ThrowsTypes=...)] type not [StashError]-attributed",
        messageFormat: "Type '{0}' in [StashFn(ThrowsTypes=...)] on method '{1}' does not have the [StashError] attribute. Only [StashError]-decorated RuntimeError subclasses are valid.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ── [StashMember] diagnostics ──────────────────────────────────────────────────────────────

    /// <summary>
    /// STASH_MEM001: [StashMember] is mutually exclusive with [StashFn].
    /// </summary>
    public static readonly DiagnosticDescriptor MemberConflictsWithFn = new(
        id: "STASH_MEM001",
        title: "[StashMember] and [StashFn] are mutually exclusive",
        messageFormat: "Method '{0}' is annotated with both [StashMember] and [StashFn]. These attributes are mutually exclusive; remove one of them.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// STASH_MEM002: [StashMember] is mutually exclusive with [StashConst].
    /// </summary>
    public static readonly DiagnosticDescriptor MemberConflictsWithConst = new(
        id: "STASH_MEM002",
        title: "[StashMember] and [StashConst] are mutually exclusive",
        messageFormat: "Symbol '{0}' is annotated with both [StashMember] and [StashConst]. These attributes are mutually exclusive; remove one of them.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// STASH_MEM003: [StashMember] method must have exactly one IInterpreterContext parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor MemberWrongParameterShape = new(
        id: "STASH_MEM003",
        title: "[StashMember] method has wrong parameter shape",
        messageFormat: "[StashMember] method '{0}' must have exactly one parameter of type IInterpreterContext. Found: {1}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// STASH_MEM004: [StashMember] method is missing a non-empty XML &lt;summary&gt; doc comment.
    /// This is an error (not a warning) — members ship without docs are a permanent paper cut.
    /// </summary>
    public static readonly DiagnosticDescriptor MemberMissingSummaryDoc = new(
        id: "STASH_MEM004",
        title: "[StashMember] missing <summary> doc comment",
        messageFormat: "[StashMember] method '{0}' is missing a non-empty XML <summary> doc comment",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// STASH_MEM005: A type in [StashMember(Throws=...)] does not have [StashError].
    /// </summary>
    public static readonly DiagnosticDescriptor MemberThrowsNotStashError = new(
        id: "STASH_MEM005",
        title: "[StashMember(Throws=...)] type not [StashError]-attributed",
        messageFormat: "Type '{0}' in [StashMember(Throws=...)] on method '{1}' does not have the [StashError] attribute. Only [StashError]-decorated RuntimeError subclasses are valid.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ── Stray-annotation diagnostic ────────────────────────────────────────────────────────────

    /// <summary>
    /// STSG014: [StashFn], [StashMember], or [StashConst] declared on a class that is not
    /// attributed with [StashNamespace]. The member is silently invisible to the generator —
    /// this diagnostic promotes that silent failure to a build error.
    /// </summary>
    public static readonly DiagnosticDescriptor StrayStdlibAnnotation = new(
        id: "STSG014",
        title: "[StashFn]/[StashMember]/[StashConst] on a non-[StashNamespace] class",
        messageFormat: "'{0}' is annotated with [{1}] but its declaring type '{2}' does not have [StashNamespace]. The annotation will be silently ignored by the source generator.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
