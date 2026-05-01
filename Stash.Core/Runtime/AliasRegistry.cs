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

        /// <summary>
        /// When <see langword="true"/> the entry is session-disabled via <c>unalias --force</c>.
        /// Disabled entries are invisible to <see cref="TryGet"/>, <see cref="Exists"/>,
        /// <see cref="All"/>, and <see cref="Names"/> but remain in the dictionary so that
        /// a subsequent <see cref="Define"/> call (REPL restart simulation) can re-enable them.
        /// </summary>
        public bool Disabled { get; set; }

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

        // Allow a Builtin→Builtin replacement unconditionally (re-registration at startup).
        // Block user→Builtin unless override: true.
        if (_entries.TryGetValue(entry.Name, out AliasEntry? existing) &&
            existing.Source == AliasSource.Builtin &&
            entry.Source != AliasSource.Builtin &&
            !entry.Override)
        {
            throw new RuntimeError(
                $"cannot override built-in alias '{entry.Name}' without AliasOptions.override = true",
                null,
                StashErrorTypes.AliasError);
        }

        _entries[entry.Name] = entry;
    }

    /// <summary>Looks up an alias entry by name. Returns <see langword="false"/> for disabled entries.</summary>
    public bool TryGet(string name, out AliasEntry? entry)
    {
        if (_entries.TryGetValue(name, out entry) && !entry.Disabled)
            return true;
        entry = null;
        return false;
    }

    /// <summary>Returns <see langword="true"/> if the name is registered and not disabled.</summary>
    public bool Exists(string name)
        => _entries.TryGetValue(name, out AliasEntry? e) && !e.Disabled;

    /// <summary>
    /// Removes a user alias by name.
    /// Returns <see langword="false"/> if the name is not registered.
    /// Throws <see cref="RuntimeError"/> with <see cref="StashErrorTypes.AliasError"/>
    /// if the entry's source is <see cref="AliasSource.Builtin"/>.
    /// </summary>
    public bool Remove(string name)
    {
        if (!_entries.TryGetValue(name, out AliasEntry? entry)) return false;

        // A force-disabled builtin is invisible; treat as not found.
        if (entry.Disabled) return false;

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
    /// </summary>
    public bool ForceRemove(string name) => _entries.Remove(name);

    /// <summary>
    /// Session-disables an alias by setting its <see cref="AliasEntry.Disabled"/> flag.
    /// The entry remains in the dictionary so that a subsequent <see cref="Define"/> call
    /// (simulating REPL restart) can re-enable it by overwriting with a fresh entry.
    /// Throws <see cref="RuntimeError"/> with <see cref="StashErrorTypes.AliasError"/> if
    /// the alias does not exist (including already-disabled entries).
    /// </summary>
    public void ForceDisable(string name)
    {
        if (!_entries.TryGetValue(name, out AliasEntry? entry) || entry.Disabled)
        {
            throw new RuntimeError(
                $"alias '{name}' is not defined",
                null,
                StashErrorTypes.AliasError);
        }
        entry.Disabled = true;
    }

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

    /// <summary>Returns all non-disabled entries sorted by name.</summary>
    public IEnumerable<AliasEntry> All()
        => _entries.Values
            .Where(e => !e.Disabled)
            .OrderBy(e => e.Name, StringComparer.Ordinal);

    /// <summary>Returns all non-disabled registered names sorted.</summary>
    public IEnumerable<string> Names()
        => _entries.Where(kv => !kv.Value.Disabled)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal);
}
