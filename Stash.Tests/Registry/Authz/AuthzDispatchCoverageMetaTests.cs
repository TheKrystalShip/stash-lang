using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Controllers;
using Stash.Tests.Registry.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// PDP-dispatch coverage meta-test: enumerates every non-<c>[PublicEndpoint]</c> action
/// across the six production registry controllers and fails if any action carries neither
/// <c>[RegistryAuthorize]</c> nor <c>[ImperativeAuthz]</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why is this a distinct gate from <c>AuthzCoverageMetaTests</c>?</b>
/// <c>AuthzCoverageMetaTests</c> verifies that every action is authentication-classified
/// (carries <c>[Authorize]</c> or <c>[PublicEndpoint]</c>).  That check says nothing about
/// whether the action actually reaches the PDP.  A developer can add a privileged endpoint
/// with <c>[Authorize]</c> alone and authentication tests stay green while the endpoint
/// performs no authorization check at all.  This meta-test closes that gap: every
/// authenticated action must carry either the declarative dispatch attribute
/// (<see cref="RegistryAuthorizeAttribute"/>) or an explicit exemption marker
/// (<see cref="ImperativeAuthzAttribute"/>) that documents the deferred work.
/// </para>
/// <para>
/// Three assertions are provided:
/// <list type="number">
///   <item><b>Production compliance</b> — all six real controllers pass.</item>
///   <item><b>Fail-path (teeth)</b> — <see cref="UnclassifiedDispatchFixtureController"/> is
///     reported; <see cref="ClassifiedDispatchFixtureController"/> is not.  The fixture
///     controller carries <c>[Authorize]</c> but no dispatch attribute, directly proving the
///     scanner catches the gap that <c>AuthzCoverageMetaTests</c> does not.</item>
///   <item><b>Imperative-pin</b> — the exact set of <c>[ImperativeAuthz]</c>-bearing
///     production actions equals <c>{PublishPackage, ClaimScope, DeleteScope}</c>.
///     Adding or removing the marker requires updating this assertion, forcing reviewer
///     attention on every future exemption.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class AuthzDispatchCoverageMetaTests
{
    // ── Registry controller types under coverage ──────────────────────────────

    private static readonly IReadOnlyList<Type> ProductionControllers =
    [
        typeof(AuthController),
        typeof(PackagesController),
        typeof(OrganizationsController),
        typeof(ScopesController),
        typeof(SearchController),
        typeof(AdminController),
    ];

    // ── The exact pinned set of imperative-exemption endpoints ────────────────

    /// <summary>
    /// The exact set of production actions allowed to carry <see cref="ImperativeAuthzAttribute"/>.
    /// Formatted as <c>ControllerName.MethodName</c>. Append-only; shrinking requires its own
    /// PDP-completion work (tracked in <c>registry-authz-pdp-completion</c>).
    /// </summary>
    private static readonly IReadOnlySet<string> PinnedImperativeActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "PackagesController.PublishPackage",
        "ScopesController.ClaimScope",
        "ScopesController.DeleteScope",
    };

    // ── Core scanning logic ───────────────────────────────────────────────────

    /// <summary>
    /// Returns all action methods on the supplied controller type using the same exclusion
    /// logic as <c>AuthzCoverageMetaTests</c>.
    /// </summary>
    private static IEnumerable<MethodInfo> GetActionMethods(Type controllerType)
    {
        var controllerBaseMethodNames = new HashSet<string>(
            typeof(ControllerBase).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(m => m.Name));
        var objectMethodNames = new HashSet<string>(
            typeof(object).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(m => m.Name));

        return controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m =>
                !m.IsStatic &&
                !m.IsAbstract &&
                !m.IsSpecialName &&
                m.GetCustomAttribute<NonActionAttribute>() == null &&
                !objectMethodNames.Contains(m.Name) &&
                !controllerBaseMethodNames.Contains(m.Name));
    }

    /// <summary>
    /// Returns <see langword="true"/> when the action is exempt from dispatch coverage
    /// because it is a public endpoint (no authentication or PDP required).
    /// </summary>
    private static bool IsPublicEndpoint(MethodInfo action) =>
        action.GetCustomAttribute<PublicEndpointAttribute>() != null;

    /// <summary>
    /// Returns <see langword="true"/> when the action satisfies dispatch coverage: it carries
    /// either <see cref="RegistryAuthorizeAttribute"/> or <see cref="ImperativeAuthzAttribute"/>.
    /// Class-level <c>[Authorize]</c> does NOT satisfy dispatch coverage.
    /// </summary>
    private static bool HasDispatchClassification(MethodInfo action) =>
        action.GetCustomAttribute<RegistryAuthorizeAttribute>() != null ||
        action.GetCustomAttribute<ImperativeAuthzAttribute>() != null;

    /// <summary>
    /// Returns a display name for an action in the form <c>ControllerName.ActionName</c>.
    /// </summary>
    private static string ActionLabel(MethodInfo action) =>
        $"{action.DeclaringType!.Name}.{action.Name}";

    // ── Assertion 1: Production compliance ───────────────────────────────────

    /// <summary>
    /// Every non-<c>[PublicEndpoint]</c> action on the six production controllers must carry
    /// either <see cref="RegistryAuthorizeAttribute"/> or <see cref="ImperativeAuthzAttribute"/>.
    /// Class-level <c>[Authorize]</c> does NOT satisfy this requirement.
    /// </summary>
    [Fact]
    public void AllProductionEndpoints_HaveDispatchClassification()
    {
        var unclassified = new List<string>();

        foreach (var controllerType in ProductionControllers)
        {
            foreach (var action in GetActionMethods(controllerType))
            {
                if (!IsPublicEndpoint(action) && !HasDispatchClassification(action))
                    unclassified.Add(ActionLabel(action));
            }
        }

        Assert.True(
            unclassified.Count == 0,
            $"{unclassified.Count} action(s) lack dispatch classification. Every non-[PublicEndpoint] " +
            $"action must carry [RegistryAuthorize] or [ImperativeAuthz(\"reason\")].\n" +
            $"Unclassified: {string.Join(", ", unclassified)}");
    }

    // ── Assertion 2: Fail-path (scanner has teeth) ────────────────────────────

    /// <summary>
    /// <see cref="UnclassifiedDispatchFixtureController"/> has an action with <c>[Authorize]</c>
    /// but no dispatch attribute — the scanner must report it, proving the gate catches the
    /// specific gap (<c>[Authorize]</c> alone) that <c>AuthzCoverageMetaTests</c> does not.
    /// </summary>
    [Fact]
    public void UnclassifiedDispatchFixture_IsDetectedByScanner()
    {
        var unclassified = GetActionMethods(typeof(UnclassifiedDispatchFixtureController))
            .Where(m => !IsPublicEndpoint(m) && !HasDispatchClassification(m))
            .Select(ActionLabel)
            .ToList();

        Assert.True(
            unclassified.Count > 0,
            "Expected UnclassifiedDispatchFixtureController to have at least one action " +
            "without a dispatch attribute, but the scanner found none. Ensure " +
            "UnclassifiedDispatchFixtureController has an [Authorize]-only action.");

        Assert.Contains(
            $"{nameof(UnclassifiedDispatchFixtureController)}.{nameof(UnclassifiedDispatchFixtureController.UnclassifiedDispatchAction)}",
            unclassified);
    }

    /// <summary>
    /// <see cref="ClassifiedDispatchFixtureController"/> produces zero unclassified actions —
    /// confirming that <see cref="RegistryAuthorizeAttribute"/>, <see cref="ImperativeAuthzAttribute"/>,
    /// and <see cref="PublicEndpointAttribute"/> are all recognised as valid dispatch classifications.
    /// </summary>
    [Fact]
    public void ClassifiedDispatchFixture_IsNotFlaggedByScanner()
    {
        var unclassified = GetActionMethods(typeof(ClassifiedDispatchFixtureController))
            .Where(m => !IsPublicEndpoint(m) && !HasDispatchClassification(m))
            .Select(ActionLabel)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            $"ClassifiedDispatchFixtureController should have zero unclassified actions, " +
            $"but found: {string.Join(", ", unclassified)}");
    }

    // ── Assertion 3: Imperative-pin ───────────────────────────────────────────

    /// <summary>
    /// The exact set of <c>[ImperativeAuthz]</c>-bearing production actions must equal
    /// <see cref="PinnedImperativeActions"/>. Adding or removing the marker on any
    /// production endpoint requires updating <see cref="PinnedImperativeActions"/>,
    /// forcing reviewer attention on every future exemption.
    /// </summary>
    [Fact]
    public void ImperativeAuthzEndpoints_MatchPinnedSet()
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);

        foreach (var controllerType in ProductionControllers)
        {
            foreach (var action in GetActionMethods(controllerType))
            {
                if (action.GetCustomAttribute<ImperativeAuthzAttribute>() != null)
                    actual.Add(ActionLabel(action));
            }
        }

        var extra = actual.Except(PinnedImperativeActions).OrderBy(s => s).ToList();
        var missing = PinnedImperativeActions.Except(actual).OrderBy(s => s).ToList();

        Assert.True(
            extra.Count == 0 && missing.Count == 0,
            $"[ImperativeAuthz] endpoint set diverged from the pinned set.\n" +
            $"Extra (added without updating the pin): {(extra.Count > 0 ? string.Join(", ", extra) : "(none)")}\n" +
            $"Missing (removed without updating the pin): {(missing.Count > 0 ? string.Join(", ", missing) : "(none)")}\n" +
            $"Update PinnedImperativeActions in {nameof(AuthzDispatchCoverageMetaTests)} and document the rationale.");
    }
}
