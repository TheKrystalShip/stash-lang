using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Cli.Shell;

namespace Stash.Tests.Cli.Completion;

/// <summary>
/// Tests for <see cref="PathExecutableCache.GetAllExecutables()"/>.
/// </summary>
public class PathExecutableCacheGetAllTests
{
    [Fact]
    public void GetAllExecutables_OnPosix_ReturnsNonEmptyList()
    {
        // Shell mode is gated off on Windows; PATH executable scanning is only
        // meaningful on POSIX where executables use the +x bit.
        if (OperatingSystem.IsWindows()) return;

        var cache = new PathExecutableCache();
        IReadOnlyList<string> executables = cache.GetAllExecutables();

        Assert.NotEmpty(executables);
    }

    [Fact]
    public void GetAllExecutables_OnPosix_ContainsCommonExecutable()
    {
        if (OperatingSystem.IsWindows()) return;

        var cache = new PathExecutableCache();
        IReadOnlyList<string> executables = cache.GetAllExecutables();

        // 'ls' is present on every POSIX system in the CI environment.
        Assert.Contains("ls", executables);
    }

    [Fact]
    public void GetAllExecutables_IsSortedAlphabetically()
    {
        if (OperatingSystem.IsWindows()) return;

        var cache = new PathExecutableCache();
        IReadOnlyList<string> executables = cache.GetAllExecutables();

        if (executables.Count < 2) return; // not enough entries to check order

        for (int i = 0; i < executables.Count - 1; i++)
        {
            int cmp = StringComparer.OrdinalIgnoreCase.Compare(executables[i], executables[i + 1]);
            Assert.True(cmp <= 0,
                $"List not sorted at index {i}: '{executables[i]}' > '{executables[i + 1]}'");
        }
    }

    [Fact]
    public void GetAllExecutables_IsDeduplicated()
    {
        if (OperatingSystem.IsWindows()) return;

        var cache = new PathExecutableCache();
        IReadOnlyList<string> executables = cache.GetAllExecutables();

        // No duplicates (case-insensitive on Windows; case-sensitive on POSIX).
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var distinct = executables.Distinct(comparer).ToList();
        Assert.Equal(distinct.Count, executables.Count);
    }

    [Fact]
    public void GetAllExecutables_CalledTwice_ReturnsSameReference()
    {
        if (OperatingSystem.IsWindows()) return;

        var cache = new PathExecutableCache();
        IReadOnlyList<string> first = cache.GetAllExecutables();
        IReadOnlyList<string> second = cache.GetAllExecutables();

        // Second call within TTL returns the cached list (same object).
        Assert.Same(first, second);
    }

    [Fact]
    public void GetAllExecutables_AfterInvalidate_RefreshesCache()
    {
        if (OperatingSystem.IsWindows()) return;

        var cache = new PathExecutableCache();
        IReadOnlyList<string> first = cache.GetAllExecutables();
        cache.Invalidate();
        IReadOnlyList<string> second = cache.GetAllExecutables();

        // After invalidation, a new list object is returned.
        Assert.NotSame(first, second);
        // But contents should still be non-empty.
        Assert.NotEmpty(second);
    }

    [Fact]
    public void GetAllExecutables_OnWindows_ReturnsEmptyOrNonEmpty()
    {
        // On Windows, shell mode is gated off but the cache still works.
        // We just ensure it doesn't throw.
        if (!OperatingSystem.IsWindows()) return;

        var cache = new PathExecutableCache();
        IReadOnlyList<string> executables = cache.GetAllExecutables();
        // List may be empty if PATHEXT/PATH is unusual, but must not throw.
        Assert.NotNull(executables);
    }
}
