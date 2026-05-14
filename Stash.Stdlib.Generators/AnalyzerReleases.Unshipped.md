; Unshipped analyzer release.
; See https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID    | Category               | Severity | Notes
-----------|------------------------|----------|------------------------------------------------------------
STASH_GEN001 | StashStdlibGenerator | Error    | Unsupported parameter type on [StashFn] method.
STASH_GEN002 | StashStdlibGenerator | Error    | Unsupported return type on [StashFn] method.
STASH_GEN003 | StashStdlibGenerator | Error    | IInterpreterContext must be the first parameter.
STASH_GEN005 | StashStdlibGenerator | Error    | Duplicate Stash function name in namespace.
STASH_GEN006 | StashStdlibGenerator | Error    | [StashConst] requires a const or static readonly field.
STASH_GEN007 | StashStdlibGenerator | Error    | Stash function name has consecutive uppercase letters.
STASH_GEN008 | StashStdlibGenerator | Error    | [StashNamespace] class must be partial and static.
STASH_GEN009 | StashStdlibGenerator | Error    | [StashStruct]/[StashEnum] type not accessible from generated Define().
STASH_DOC001 | StashStdlibGenerator | Warning  | [StashFn] missing <summary> doc comment.
STASH_DOC002 | StashStdlibGenerator | Warning  | [StashFn] parameter missing <param> doc comment.
STSG010    | StashStdlibGenerator   | Warning  | Throws metadata mismatch between attribute and doc comments.
STSG011    | StashStdlibGenerator   | Info     | <exception cref> uses old StashErrorTypes field form.
STSG013    | StashStdlibGenerator   | Error    | [StashFn(ThrowsTypes=...)] type not [StashError]-attributed.
STSE001    | StashErrorGenerator    | Error    | [StashError] class must be in Stash.Runtime.Errors namespace.
STSE002    | StashErrorGenerator    | Error    | [StashError] class must inherit RuntimeError.
STSE003    | StashErrorGenerator    | Error    | [StashError] class declares a reserved member name.
STSE004    | StashErrorGenerator    | Error    | Duplicate canonical name across [StashError] classes.
