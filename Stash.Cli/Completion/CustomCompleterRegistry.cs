using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;

namespace Stash.Cli.Completion;

/// <summary>
/// Thread-safe registry of user-registered custom completers.
/// Maps command names to Stash callable functions.
/// </summary>
internal sealed class CustomCompleterRegistry
{
    private readonly Dictionary<string, IStashCallable> _completers =
        new(StringComparer.Ordinal);

    private readonly HashSet<string> _reportedErrors =
        new(StringComparer.Ordinal);

    private readonly object _lock = new();

    /// <summary>Registers <paramref name="fn"/> as the completer for <paramref name="name"/>. Re-registering replaces.</summary>
    public void Register(string name, IStashCallable fn)
    {
        lock (_lock)
            _completers[name] = fn;
    }

    /// <summary>Removes the completer for <paramref name="name"/>. Returns <c>true</c> if one was registered.</summary>
    public bool Unregister(string name)
    {
        lock (_lock)
            return _completers.Remove(name);
    }

    /// <summary>Returns the registered completer for <paramref name="name"/>, or <c>null</c> if none.</summary>
    public IStashCallable? Get(string name)
    {
        lock (_lock)
            return _completers.TryGetValue(name, out var fn) ? fn : null;
    }

    /// <summary>Returns all registered command names sorted alphabetically (case-insensitive).</summary>
    public IReadOnlyList<string> RegisteredNames()
    {
        lock (_lock)
            return _completers.Keys
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    /// <summary>Records that an error has been logged for <paramref name="name"/> this session.</summary>
    public void RecordError(string name)
    {
        lock (_lock)
            _reportedErrors.Add(name);
    }

    /// <summary>Returns <c>true</c> if an error has already been reported for <paramref name="name"/>.</summary>
    public bool HasReportedError(string name)
    {
        lock (_lock)
            return _reportedErrors.Contains(name);
    }
}
