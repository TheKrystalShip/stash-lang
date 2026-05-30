using Microsoft.AspNetCore.Mvc;

namespace Stash.Tests.Registry.Fixtures;

/// <summary>
/// Fixture controller that intentionally has an unclassified action (neither
/// <c>[Authorize]</c> nor <c>[PublicEndpoint]</c>) so that the
/// AuthzCoverageMetaTests fail-path test can assert the meta-test catches it.
/// </summary>
[ApiController]
[Route("api/v1/fixture-unclassified")]
public class UnclassifiedEndpointFixtureController : ControllerBase
{
    /// <summary>
    /// An action with no authorization annotation — intentionally unclassified.
    /// </summary>
    [HttpGet("probe")]
    public IActionResult UnclassifiedAction() => Ok();
}
