using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Controllers;
using Stash.Tests.Registry.Fixtures;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Default-deny meta-test: enumerates every controller action in the Stash.Registry
/// assembly and fails if any action is neither <c>[Authorize(...)]</c> nor
/// <c>[PublicEndpoint(...)]</c>.
/// </summary>
/// <remarks>
/// <para>
/// An endpoint is "classified" when either:
/// <list type="bullet">
///   <item><description>The action method carries <c>[Authorize]</c> (any overload), OR</description></item>
///   <item><description>The action method's declaring controller class carries <c>[Authorize]</c>, OR</description></item>
///   <item><description>The action method carries <c>[PublicEndpoint]</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// The production-compliance assertions scan only the six real registry controllers
/// (<see cref="AuthController"/>, <see cref="PackagesController"/>,
/// <see cref="OrganizationsController"/>, <see cref="ScopesController"/>,
/// <see cref="SearchController"/>, <see cref="AdminController"/>).
/// The fixture-controller assertions target the two test-only fixtures directly.
/// </para>
/// </remarks>
public sealed class AuthzCoverageMetaTests
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

    // ── Core scanning logic ───────────────────────────────────────────────────

    /// <summary>
    /// Returns all action methods on the supplied controller type.
    /// </summary>
    /// <remarks>
    /// An action method is a public, non-static, non-abstract instance method that
    /// is NOT decorated with <c>[NonAction]</c> and is not an inherited
    /// <see cref="object"/> or <see cref="ControllerBase"/> member.
    /// </remarks>
    private static IEnumerable<MethodInfo> GetActionMethods(Type controllerType)
    {
        // ControllerBase methods we want to exclude by name (standard object + controller infrastructure)
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
    /// Returns <see langword="true"/> when the action (or its declaring controller class)
    /// carries an explicit authorization classification.
    /// </summary>
    private static bool IsClassified(MethodInfo action)
    {
        bool hasAuthorizeOnAction = action.GetCustomAttribute<AuthorizeAttribute>() != null;
        bool hasPublicEndpointOnAction = action.GetCustomAttribute<PublicEndpointAttribute>() != null;
        bool hasAuthorizeOnController = action.DeclaringType!.GetCustomAttribute<AuthorizeAttribute>() != null;

        return hasAuthorizeOnAction || hasPublicEndpointOnAction || hasAuthorizeOnController;
    }

    /// <summary>
    /// Returns a display name for an action in the form <c>ControllerName.ActionName</c>.
    /// </summary>
    private static string ActionLabel(MethodInfo action) =>
        $"{action.DeclaringType!.Name}.{action.Name}";

    // ── Production compliance (happy path) ───────────────────────────────────

    /// <summary>
    /// Asserts that every action on every production registry controller is
    /// explicitly classified. Fails with the full list of unclassified endpoints.
    /// </summary>
    [Fact]
    public void AllProductionEndpoints_AreExplicitlyClassified()
    {
        var unclassified = new List<string>();

        foreach (var controllerType in ProductionControllers)
        {
            foreach (var action in GetActionMethods(controllerType))
            {
                if (!IsClassified(action))
                    unclassified.Add(ActionLabel(action));
            }
        }

        Assert.True(
            unclassified.Count == 0,
            $"{unclassified.Count} unclassified endpoint(s) detected. Every action must carry " +
            $"[Authorize(...)] or [PublicEndpoint(\"...\")].\n" +
            $"Unclassified: {string.Join(", ", unclassified)}");
    }

    // ── Fixture: unclassified controller IS flagged ───────────────────────────

    /// <summary>
    /// Asserts that <see cref="UnclassifiedEndpointFixtureController"/> has at least
    /// one action flagged as unclassified when scanned by the same logic used for
    /// production controllers.
    /// </summary>
    [Fact]
    public void UnclassifiedFixtureController_IsDetectedByScanner()
    {
        var unclassified = GetActionMethods(typeof(UnclassifiedEndpointFixtureController))
            .Where(m => !IsClassified(m))
            .Select(ActionLabel)
            .ToList();

        Assert.True(
            unclassified.Count > 0,
            "Expected UnclassifiedEndpointFixtureController to have at least one unclassified " +
            "action so the fail-path test is meaningful, but the scanner found none. " +
            "Ensure UnclassifiedEndpointFixtureController has an action with neither " +
            "[Authorize] nor [PublicEndpoint].");

        // The specific unclassified action must appear in the report
        Assert.Contains(
            $"{nameof(UnclassifiedEndpointFixtureController)}.{nameof(UnclassifiedEndpointFixtureController.UnclassifiedAction)}",
            unclassified);
    }

    // ── Fixture: fully-classified controller is NOT flagged ──────────────────

    /// <summary>
    /// Asserts that <see cref="ClassifiedEndpointFixtureController"/> produces zero
    /// unclassified actions — confirming both <c>[PublicEndpoint]</c> and
    /// <c>[Authorize]</c> are recognised as valid classifications.
    /// </summary>
    [Fact]
    public void ClassifiedFixtureController_IsNotFlaggedByScanner()
    {
        var unclassified = GetActionMethods(typeof(ClassifiedEndpointFixtureController))
            .Where(m => !IsClassified(m))
            .Select(ActionLabel)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            $"ClassifiedEndpointFixtureController should have zero unclassified actions, " +
            $"but found: {string.Join(", ", unclassified)}");
    }
}
