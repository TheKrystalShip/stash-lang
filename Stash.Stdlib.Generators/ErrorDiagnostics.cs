namespace Stash.Stdlib.Generators;

using Microsoft.CodeAnalysis;

internal static class ErrorDiagnostics
{
    private const string Category = "StashErrorGenerator";

    public static readonly DiagnosticDescriptor WrongNamespace = new(
        id: "STSE001",
        title: "[StashError] class must be in Stash.Runtime.Errors namespace",
        messageFormat: "[StashError] class '{0}' must be declared in the '{1}' namespace",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MustInheritRuntimeError = new(
        id: "STSE002",
        title: "[StashError] class must inherit RuntimeError",
        messageFormat: "[StashError] class '{0}' must directly or indirectly inherit from '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ReservedMemberName = new(
        id: "STSE003",
        title: "[StashError] class declares a reserved member name",
        messageFormat: "[StashError] class '{0}' declares member '{1}', which is reserved. The canonical name is always derived from the C# class name; do not store it in a field or property.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateCanonicalName = new(
        id: "STSE004",
        title: "Duplicate canonical name across [StashError] classes",
        messageFormat: "[StashError] canonical name '{0}' is already claimed by class '{1}'. Use [StashError(Name = \"...\")] to disambiguate, or rename one of the classes.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // TODO STSE005: Properties-vs-GetProperties consistency check (deferred to Phase B).
}
