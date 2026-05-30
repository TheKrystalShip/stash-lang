using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Stash.Registry.Auth.Authorization;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Unit tests for <see cref="RegistryActionResourceResolver"/>.
/// </summary>
public sealed class RegistryActionResourceResolverTests
{
    // ── ClaimScope throws (Construct-level enforcement) ───────────────────────

    /// <summary>
    /// <see cref="RegistryAction.ClaimScope"/> is intentionally absent from
    /// <see cref="RegistryActionResourceResolver.Resolve"/>'s switch because it is an
    /// <c>[ImperativeAuthz]</c> endpoint: its scope/owner/ownerType fields come from the
    /// JSON request body, not from route values, so the pure-route resolver cannot build a
    /// <see cref="ScopeResource"/> for it.
    ///
    /// This test pins that reaching the default arm (i.e., a future developer mistakenly
    /// placing <c>[RegistryAuthorize(RegistryAction.ClaimScope)]</c> on the endpoint) throws
    /// <see cref="InvalidOperationException"/> at request time — failing loud rather than
    /// silently producing a <c>ScopeResource("")</c> with an empty scope name.
    /// </summary>
    [Fact]
    public void Resolve_ClaimScope_ThrowsInvalidOperationException()
    {
        var routeValues = new RouteValueDictionary();
        var httpContext = new DefaultHttpContext();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RegistryActionResourceResolver.Resolve(
                RegistryAction.ClaimScope,
                routeValues,
                httpContext));

        // The message must mention ClaimScope or the default-arm diagnostic text.
        Assert.True(
            ex.Message.Contains("ClaimScope") ||
            ex.Message.Contains("Add an entry to RegistryActionResourceResolver.Resolve"),
            $"Expected exception message to identify ClaimScope or the missing-entry text, but got: {ex.Message}");
    }
}
