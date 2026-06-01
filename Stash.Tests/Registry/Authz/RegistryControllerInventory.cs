using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Controllers;
using Stash.Tests.Registry.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Single source of truth for the set of production registry controllers scanned by the
/// authorization meta-tests (<c>AuthzCoverageMetaTests</c> and
/// <see cref="AuthzDispatchCoverageMetaTests"/>).
/// </summary>
/// <remarks>
/// <para>
/// The set is <b>derived by reflection</b> over the <c>Stash.Registry</c> assembly rather
/// than hand-maintained, so a newly added controller is automatically held to both
/// authorization gates. This makes the omission "added a controller, forgot to register it
/// with the gate" <b>impossible</b> (Construct) rather than merely detectable (Detect) — the
/// gates previously hardcoded an identical six-entry list in two places, and a seventh
/// controller would have escaped both silently.
/// </para>
/// <para>
/// Derivation has exactly one failure mode: a vacuous result. If the reflection sweep returns
/// an empty (or under-scoped) set, every gate that iterates it passes trivially. The
/// <see cref="FloorCount"/> guard and the teeth tests in <see cref="RegistryControllerInventoryTests"/>
/// close that hole — mirroring the floor-guard discipline in <c>NoMagicAuthStringsMetaTests</c>.
/// </para>
/// </remarks>
internal static class RegistryControllerInventory
{
    /// <summary>
    /// Minimum number of production controllers expected in the <c>Stash.Registry</c> assembly.
    /// A reflection sweep returning fewer than this fails <see cref="RegistryControllerInventoryTests.Production_MeetsFloorGuard"/>,
    /// catching an empty or under-scoped result before it can vacuously satisfy the authz gates.
    /// Lower this only when the production controller set legitimately shrinks below it.
    /// </summary>
    public const int FloorCount = 6;

    /// <summary>
    /// Every concrete, top-level public <see cref="ControllerBase"/> subclass defined in the
    /// <c>Stash.Registry</c> assembly, ordered by name.
    /// </summary>
    /// <remarks>
    /// Scoped to the <c>Stash.Registry</c> assembly precisely (via <see cref="AuthController"/>'s
    /// assembly) so that test-only fixture controllers — which live in <c>Stash.Tests</c> and are
    /// deliberately unclassified — are never swept in. If a fixture ever appears here, the fix is
    /// to tighten the assembly scope, never to add a hand-maintained exclusion list (that would
    /// reintroduce the very defect this type removes).
    /// </remarks>
    public static IReadOnlyList<Type> Production { get; } =
        typeof(AuthController).Assembly
            .GetTypes()
            .Where(t => t.IsClass
                     && !t.IsAbstract
                     && t.IsPublic
                     && typeof(ControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();
}

/// <summary>
/// Teeth for <see cref="RegistryControllerInventory"/>: proves the reflection-derived controller
/// set is non-vacuous and correctly scoped, so the gates that consume it cannot pass trivially.
/// </summary>
public sealed class RegistryControllerInventoryTests
{
    /// <summary>
    /// The reflection sweep must discover at least <see cref="RegistryControllerInventory.FloorCount"/>
    /// controllers. An empty/under-scoped result would otherwise satisfy every authz gate vacuously.
    /// </summary>
    [Fact]
    public void Production_MeetsFloorGuard()
    {
        Assert.True(
            RegistryControllerInventory.Production.Count >= RegistryControllerInventory.FloorCount,
            $"Reflection discovered {RegistryControllerInventory.Production.Count} production controller(s) " +
            $"in the Stash.Registry assembly, fewer than the floor of {RegistryControllerInventory.FloorCount}. " +
            $"A vacuous or under-scoped sweep would silently pass every authorization gate. " +
            $"If the controller set legitimately shrank, lower RegistryControllerInventory.FloorCount.");
    }

    /// <summary>
    /// Every known production controller must appear in the derived set — proving the predicate
    /// actually captures real controllers (a too-strict predicate returning nothing would pass
    /// the floor guard only by accident and silently neuter the consuming gates).
    /// </summary>
    [Fact]
    public void Production_ContainsKnownControllers()
    {
        Type[] known =
        [
            typeof(AuthController),
            typeof(PackagesController),
            typeof(OrganizationsController),
            typeof(ScopesController),
            typeof(SearchController),
            typeof(AdminController),
        ];

        var missing = known.Where(t => !RegistryControllerInventory.Production.Contains(t)).ToList();

        Assert.True(
            missing.Count == 0,
            $"The derived production controller set is missing known controller(s): " +
            $"{string.Join(", ", missing.Select(t => t.Name))}. The reflection predicate in " +
            $"RegistryControllerInventory.Production no longer captures every real controller.");
    }

    /// <summary>
    /// Deliberately-unclassified test fixture controllers live in <c>Stash.Tests</c>; scoping the
    /// sweep to the <c>Stash.Registry</c> assembly must keep them out. If they leak in, the
    /// dispatch gate would false-positive on fixtures designed to be unclassified.
    /// </summary>
    [Fact]
    public void Production_ExcludesTestFixtureControllers()
    {
        Assert.DoesNotContain(typeof(UnclassifiedDispatchFixtureController), RegistryControllerInventory.Production);
        Assert.DoesNotContain(typeof(UnclassifiedEndpointFixtureController), RegistryControllerInventory.Production);
        Assert.DoesNotContain(typeof(ClassifiedDispatchFixtureController), RegistryControllerInventory.Production);
        Assert.DoesNotContain(typeof(ClassifiedEndpointFixtureController), RegistryControllerInventory.Production);
    }
}
