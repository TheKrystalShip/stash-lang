using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Controllers;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Structural meta-test: every action method on <see cref="PackagesController"/> must carry
/// <c>[RegistryAuthorize]</c>, with no exceptions — including actions that also carry
/// <c>[PublicEndpoint]</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why Detect, not Construct?</b>
/// <c>[PublicEndpoint]</c> is the legitimate opt-out for unauthenticated callers (public packages
/// allow anonymous reads without a bearer token). However, <c>[PublicEndpoint]</c> alone still
/// satisfies <b>both</b> existing structural gates:
/// <list type="bullet">
///   <item><c>AuthzCoverageMetaTests</c> — requires each action to be authentication-classified;
///     <c>[PublicEndpoint]</c> satisfies this requirement by design.</item>
///   <item><c>AuthzDispatchCoverageMetaTests</c> — exempts <c>[PublicEndpoint]</c> actions
///     from the PDP-dispatch requirement; they need no dispatch attribute.</item>
/// </list>
/// This means a future developer could add a new read sub-resource (e.g. a
/// <c>GET …/versions</c> or <c>GET …/readme</c> endpoint) with only <c>[PublicEndpoint]</c>,
/// receive a green CI build, and silently ship a private-package existence leak — the PDP gate
/// that maps <see cref="AuthzDenyReason.VisibilityHidden"/> to 404 would never run.
/// </para>
/// <para>
/// The type system cannot forbid this: <c>[PublicEndpoint]</c> is a valid attribute, and no
/// compiler-level constraint can express "must also carry <c>[RegistryAuthorize]</c>." This
/// gate is therefore the strongest available prevention — an explicit Detect test that fails
/// immediately when any <c>PackagesController</c> action lacks <c>[RegistryAuthorize]</c>,
/// regardless of which other attributes are present. Ships with a fail-path fixture and a count
/// floor that prevents the vacuous-pass failure mode.
/// </para>
/// <para>
/// Three assertions are provided:
/// <list type="number">
///   <item><b>Production compliance</b> — every action on <see cref="PackagesController"/>
///     carries <c>[RegistryAuthorize]</c>. Verified to be green against the current controller
///     (all existing actions already carry the attribute).</item>
///   <item><b>Non-vacuity floor</b> — the action count meets or exceeds a minimum threshold,
///     preventing a silent sweep-returns-nothing pass. Uses <c>&gt;=</c> (not <c>==</c>) so
///     new actions added by P3/P4 are automatically held to the same invariant without requiring
///     a pin update.</item>
///   <item><b>Fail-path fixture (scanner has teeth)</b> — <see cref="PublicEndpointOnlyFixtureController"/>
///     declares an action with <c>[PublicEndpoint]</c> but no <c>[RegistryAuthorize]</c>; the
///     scanner must flag it. This proves the gate catches the specific blind spot in
///     <c>AuthzCoverageMetaTests</c> and <c>AuthzDispatchCoverageMetaTests</c>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class PackagesControllerRegistryAuthorizeRequiredTests
{
    // ── Floor guard ───────────────────────────────────────────────────────────

    /// <summary>
    /// Minimum number of action methods expected on <see cref="PackagesController"/>.
    /// Prevents a vacuous pass when the reflection sweep returns an empty set.
    /// Use <c>&gt;=</c> so new actions added in future phases are automatically held
    /// to <c>[RegistryAuthorize]</c> coverage without requiring a pin update here.
    /// </summary>
    private const int MinActionCount = 13;

    // ── Core scanning logic ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the public, declared, non-special action methods of the supplied controller type,
    /// excluding inherited infrastructure from <see cref="ControllerBase"/> and <see cref="object"/>.
    /// </summary>
    private static IReadOnlyList<MethodInfo> GetActionMethods(System.Type controllerType)
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
                !controllerBaseMethodNames.Contains(m.Name))
            .ToArray();
    }

    /// <summary>
    /// Returns <see langword="true"/> when the action carries <c>[RegistryAuthorize]</c>.
    /// <c>[PublicEndpoint]</c> is deliberately NOT accepted as an exemption — see class
    /// remarks for the rationale.
    /// </summary>
    private static bool HasRegistryAuthorize(MethodInfo action) =>
        action.GetCustomAttribute<RegistryAuthorizeAttribute>() != null;

    /// <summary>
    /// Returns a display name for an action in the form <c>ControllerName.ActionName</c>.
    /// </summary>
    private static string ActionLabel(MethodInfo action) =>
        $"{action.DeclaringType!.Name}.{action.Name}";

    // ── Assertion 1: Production compliance ───────────────────────────────────

    /// <summary>
    /// Every action on <see cref="PackagesController"/> must carry <c>[RegistryAuthorize]</c>.
    /// <c>[PublicEndpoint]</c> alone does NOT satisfy this requirement.
    /// </summary>
    [Fact]
    public void AllPackagesControllerActions_CarryRegistryAuthorize()
    {
        var actions = GetActionMethods(typeof(PackagesController));
        var missing = actions
            .Where(m => !HasRegistryAuthorize(m))
            .Select(ActionLabel)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} PackagesController action(s) lack [RegistryAuthorize]. " +
            $"Every action on PackagesController must carry [RegistryAuthorize] — including " +
            $"public read endpoints that also carry [PublicEndpoint] — because [PublicEndpoint] " +
            $"alone bypasses the PDP visibility gate and would silently expose private package " +
            $"metadata to anonymous callers.\n" +
            $"Missing: {string.Join(", ", missing)}");
    }

    // ── Assertion 2: Non-vacuity floor ────────────────────────────────────────

    /// <summary>
    /// The reflection sweep must discover at least <see cref="MinActionCount"/> actions on
    /// <see cref="PackagesController"/>. A smaller count means the sweep returned a truncated
    /// or empty set and would have silently passed Assertion 1.
    /// </summary>
    [Fact]
    public void PackagesController_MeetsActionCountFloor()
    {
        var count = GetActionMethods(typeof(PackagesController)).Count;

        Assert.True(
            count >= MinActionCount,
            $"PackagesController reflection discovered only {count} action method(s), " +
            $"fewer than the floor of {MinActionCount}. A vacuous or under-scoped sweep " +
            $"would have silently satisfied AllPackagesControllerActions_CarryRegistryAuthorize. " +
            $"If the controller legitimately shrank, lower MinActionCount.");
    }

    // ── Assertion 3: Fail-path fixture (scanner has teeth) ────────────────────

    /// <summary>
    /// <see cref="PublicEndpointOnlyFixtureController"/> has an action with <c>[PublicEndpoint]</c>
    /// but no <c>[RegistryAuthorize]</c>. The scanner must flag it, proving the gate catches
    /// the specific blind spot in <c>AuthzCoverageMetaTests</c> and
    /// <c>AuthzDispatchCoverageMetaTests</c> (both of which accept <c>[PublicEndpoint]</c> alone).
    /// </summary>
    [Fact]
    public void PublicEndpointOnlyFixture_IsDetectedByScanner()
    {
        var missing = GetActionMethods(typeof(PublicEndpointOnlyFixtureController))
            .Where(m => !HasRegistryAuthorize(m))
            .Select(ActionLabel)
            .ToList();

        Assert.True(
            missing.Count > 0,
            "Expected PublicEndpointOnlyFixtureController to have at least one action " +
            "without [RegistryAuthorize], but the scanner found none. Ensure " +
            "PublicEndpointOnlyFixtureController has a [PublicEndpoint]-only action.");

        Assert.Contains(
            $"{nameof(PublicEndpointOnlyFixtureController)}.{nameof(PublicEndpointOnlyFixtureController.PublicEndpointOnlyAction)}",
            missing);
    }

    /// <summary>
    /// A <see cref="PackagesController"/> action that carries both <c>[PublicEndpoint]</c>
    /// and <c>[RegistryAuthorize]</c> is NOT flagged — the dual-attribute shape is the correct
    /// invariant for public read endpoints on PackagesController.
    /// </summary>
    [Fact]
    public void DualAttributeFixture_IsNotFlaggedByScanner()
    {
        var missing = GetActionMethods(typeof(DualAttributeFixtureController))
            .Where(m => !HasRegistryAuthorize(m))
            .Select(ActionLabel)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"DualAttributeFixtureController should have zero actions without [RegistryAuthorize], " +
            $"but found: {string.Join(", ", missing)}");
    }
}

// ── Test-file fixtures ────────────────────────────────────────────────────────

/// <summary>
/// Fixture controller that intentionally has an action carrying <c>[PublicEndpoint]</c> but
/// no <c>[RegistryAuthorize]</c>. Used by
/// <see cref="PackagesControllerRegistryAuthorizeRequiredTests.PublicEndpointOnlyFixture_IsDetectedByScanner"/>
/// to prove the scanner flags this pattern — the exact blind spot that both
/// <c>AuthzCoverageMetaTests</c> and <c>AuthzDispatchCoverageMetaTests</c> do not catch.
/// </summary>
[ApiController]
[Route("api/v1/fixture-public-endpoint-only")]
public class PublicEndpointOnlyFixtureController : ControllerBase
{
    /// <summary>
    /// An action that carries <c>[PublicEndpoint]</c> but no <c>[RegistryAuthorize]</c> —
    /// intentionally missing the PDP dispatch attribute.
    /// </summary>
    [PublicEndpoint("test fixture — intentionally missing [RegistryAuthorize]")]
    [HttpGet("probe")]
    public IActionResult PublicEndpointOnlyAction() => Ok();
}

/// <summary>
/// Fixture controller that has an action carrying both <c>[PublicEndpoint]</c> and
/// <c>[RegistryAuthorize]</c> — the correct dual-attribute shape for public read endpoints
/// on <see cref="PackagesController"/>. Used by
/// <see cref="PackagesControllerRegistryAuthorizeRequiredTests.DualAttributeFixture_IsNotFlaggedByScanner"/>
/// to confirm the scanner accepts this combination.
/// </summary>
[ApiController]
[Route("api/v1/fixture-dual-attribute")]
public class DualAttributeFixtureController : ControllerBase
{
    /// <summary>
    /// An action with both <c>[PublicEndpoint]</c> and <c>[RegistryAuthorize]</c> — satisfies
    /// the invariant, must not be flagged.
    /// </summary>
    [PublicEndpoint("test fixture — public but still PDP-gated")]
    [RegistryAuthorize(RegistryAction.ReadPackageMetadata)]
    [HttpGet("probe")]
    public IActionResult DualAttributeAction() => Ok();
}
