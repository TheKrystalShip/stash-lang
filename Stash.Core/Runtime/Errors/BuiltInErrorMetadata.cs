namespace Stash.Runtime.Errors;

using System;
using System.Collections.Generic;

/// <summary>
/// Immutable metadata record for a single built-in Stash error type, emitted by the
/// <c>StashErrorRegistryGenerator</c> source generator.
/// </summary>
/// <param name="Name">The canonical Stash-facing name (e.g. <c>"IOError"</c>).</param>
/// <param name="ClrType">The CLR type of the error class (e.g. <c>typeof(IOError)</c>).</param>
/// <param name="Properties">The names of extra Stash-accessible properties beyond the base <c>message</c>/<c>type</c>/<c>stack</c> fields.</param>
/// <param name="PropertyTypes">Stash-facing type label for each entry in <see cref="Properties"/>, in the same order.</param>
/// <param name="Description">Short description of when this error is thrown, for reference documentation.</param>
public sealed record BuiltInErrorMetadata(
    string Name,
    Type ClrType,
    IReadOnlyList<string> Properties,
    IReadOnlyList<string> PropertyTypes,
    string Description);
