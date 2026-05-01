namespace Stash.Runtime;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Process-local registry of alias definitions held on each VM instance.
/// Stores template aliases (string body with <c>${...}</c> placeholders) and function
/// aliases (first-class <see cref="IStashCallable"/> closures) by name.
/// </summary>
public sealed class AliasRegistry
{
    /// <summary>Distinguishes string-template aliases from function-closure aliases.</summary>
    public enum AliasKind { Template, Function }

    /// <summary>How the alias was introduced into the registry.</summary>
    public enum AliasSource { Rc, Repl, Saved, Builtin }

    /// <summary>A single registered alias entry.</summary>
    public sealed class AliasEntry
    {
        public required string Name { get; init; }
        public required AliasKind Kind { get; init; }

        /// <summary>Body string for Template aliases. <c>null</c> for Function aliases.</summary>
        public string? TemplateBody { get; set; }

        /// <summary>Callable body for Function aliases. <c>null</c> for Template aliases.</summary>
        public IStashCallable? FunctionBody { get; init; }

        public string? Description { get; set; }

        /// <summary>Optional pre-invocation hook; wired in Phase E.</summary>
        public IStashCallable? Before { get; set; }

        /// <summary>Optional post-invocation hook; wired in Phase E.</summary>
        public IStashCallable? After { get; set; }

        /// <summary>Confirmation prompt text; wired in Phase E.</summary>
        public string? Confirm { get; set; }

        public AliasSource Source { get; set; } = AliasSource.Repl;

        /// <summary>Whether this entry is permitted to shadow a built-in alias of the same name.</summary>
        public bool Override { get; set; }

        public string? SourceFile { get; set; }
        public int? SourceLine { get; set; }
    }

    private static readonly Regex _validName = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly Dictionary<string, AliasEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers an alias. Last-wins; if an existing entry's source is
    /// <see cref="AliasSource.Builtin"/> and <paramref name="entry"/>.Override is
    /// <see langword="false"/>, throws a <see cref="RuntimeError"/> with error type
    /// <see cref="StashErrorTypes.AliasError"/>.
    /// </summary>
    public void Define(AliasEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Name) || !_validName.IsMatch(entry.Name))
        {
            throw new RuntimeError(
                $"alias name '{entry.Name}' is not a valid identifier; must match [A-Za-z_][A-Za-z0-9_]*",
                null,
                StashErrorTypes.AliasError);
        }

        if (_entries.TryGetValue(entry.Name, out AliasEntry? existing) &&
            existing.Source == AliasSource.Builtin &&
            !entry.Override)
        {
            throw new RuntimeError(
                $"cannot override built-in alias '{entry.Name}' without AliasOptions.override = true",
                null,
                StashErrorTypes.AliasError);
        }

        _entries[entry.Name] = entry;
    }

    /// <summary>Looks up an alias entry by name.</summary>
    public bool TryGet(string name, out AliasEntry? entry)
        => _entries.TryGetValue(name, out entry);

    /// <summary>Returns <see langword="true"/> if the name is registered.</summary>
    public bool Exists(string name) => _entries.ContainsKey(name);

    /// <summary>
    /// Removes a user alias by name.
    /// Returns <see langword="false"/> if the name is not registered.
    /// Throws <see cref="RuntimeError"/> with <see cref="StashErrorTypes.AliasError"/>
    /// if the entry's source is <see cref="AliasSource.Builtin"/>.
    /// </summary>
    public bool Remove(string name)
    {
        if (!_entries.TryGetValue(name, out AliasEntry? entry)) return false;

        if (entry.Source == AliasSource.Builtin)
        {
            throw new RuntimeError(
                $"cannot remove built-in alias '{name}'; use 'unalias --force' to disable",
                null,
                StashErrorTypes.AliasError);
        }

        return _entries.Remove(name);
    }

    /// <summary>
    /// Removes an alias unconditionally, bypassing the built-in guard.
    /// Used by the <c>unalias --force</c> flag (Phase C).
    /// </summary>
    public bool ForceRemove(string name) => _entries.Remove(name);

    /// <summary>
    /// Removes all non-Builtin aliases and returns the count removed.
    /// </summary>
    public int Clear()
    {
        var toRemove = _entries
            .Where(kv => kv.Value.Source != AliasSource.Builtin)
            .Select(kv => kv.Key)
            .ToList();

        foreach (string name in toRemove)
            _entries.Remove(name);

        return toRemove.Count;
    }

    /// <summary>Returns all entries sorted by name.</summary>
    public IEnumerable<AliasEntry> All()
        => _entries.Values.OrderBy(e => e.Name, StringComparer.Ordinal);

    /// <summary>Returns all registered names sorted.</summary>
    public IEnumerable<string> Names()
        => _entries.Keys.OrderBy(k => k, StringComparer.Ordinal);
}
