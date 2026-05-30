using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Auth.Authorization;

namespace Stash.Tests.Registry.Fixtures;

/// <summary>
/// Fixture controller where every action is explicitly classified (one with
/// <c>[PublicEndpoint]</c>, one with <c>[Authorize]</c>) so that the
/// AuthzCoverageMetaTests happy-path test can confirm neither trips the meta-test.
/// </summary>
[ApiController]
[Route("api/v1/fixture-classified")]
public class ClassifiedEndpointFixtureController : ControllerBase
{
    /// <summary>
    /// An action explicitly marked public.
    /// </summary>
    [PublicEndpoint("test fixture — intentionally public")]
    [HttpGet("public")]
    public IActionResult PublicAction() => Ok();

    /// <summary>
    /// An action explicitly requiring authorization.
    /// </summary>
    [Authorize]
    [HttpGet("protected")]
    public IActionResult ProtectedAction() => Ok();
}
