using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Stash.Tests.Registry.Fixtures;

/// <summary>
/// Fixture controller that intentionally has an action which is authenticated
/// (carries <c>[Authorize]</c>) but lacks both <c>[RegistryAuthorize]</c> and
/// <c>[ImperativeAuthz]</c>, proving that <c>AuthzDispatchCoverageMetaTests</c>
/// catches the gap that <c>AuthzCoverageMetaTests</c> does not.
/// </summary>
[ApiController]
[Route("api/v1/fixture-unclassified-dispatch")]
public class UnclassifiedDispatchFixtureController : ControllerBase
{
    /// <summary>
    /// An action that carries <c>[Authorize]</c> but no dispatch attribute —
    /// intentionally unclassified for dispatch coverage.
    /// </summary>
    [Authorize]
    [HttpGet("probe")]
    public IActionResult UnclassifiedDispatchAction() => Ok();
}
