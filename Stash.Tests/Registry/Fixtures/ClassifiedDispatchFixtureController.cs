using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Auth.Authorization;

namespace Stash.Tests.Registry.Fixtures;

/// <summary>
/// Fixture controller where every non-<c>[PublicEndpoint]</c> action carries at least one
/// dispatch attribute — either <c>[RegistryAuthorize]</c> or <c>[ImperativeAuthz]</c> —
/// so that <c>AuthzDispatchCoverageMetaTests</c> reports zero unclassified actions.
/// </summary>
[ApiController]
[Route("api/v1/fixture-classified-dispatch")]
public class ClassifiedDispatchFixtureController : ControllerBase
{
    /// <summary>
    /// An action that carries <c>[PublicEndpoint]</c> — exempt from dispatch coverage.
    /// </summary>
    [PublicEndpoint("test fixture — intentionally public")]
    [HttpGet("public")]
    public IActionResult PublicAction() => Ok();

    /// <summary>
    /// An action that carries <c>[RegistryAuthorize]</c> — satisfies dispatch coverage.
    /// </summary>
    [Authorize]
    [RegistryAuthorize(RegistryAction.Whoami)]
    [HttpGet("declarative")]
    public IActionResult DeclarativeAction() => Ok();

    /// <summary>
    /// An action that carries <c>[ImperativeAuthz]</c> — satisfies dispatch coverage as
    /// an auditable exemption.
    /// </summary>
    [Authorize]
    [ImperativeAuthz("test fixture — demonstrates ImperativeAuthz satisfies dispatch coverage")]
    [HttpGet("imperative")]
    public IActionResult ImperativeAction() => Ok();
}
