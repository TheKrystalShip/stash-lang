using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Stash.Registry;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Registry.Validation;

/// <summary>
/// Tests the <see cref="Startup.BuildInvalidModelStateResponse"/> factory that
/// aggregates all <c>ModelState</c> error messages into a single
/// <see cref="ErrorResponse"/> with <c>Error = "InvalidRequest"</c>.
/// </summary>
public sealed class InvalidModelStateFactoryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ActionContext MakeContext(IEnumerable<(string field, string message)> errors)
    {
        var modelState = new ModelStateDictionary();
        foreach (var (field, message) in errors)
            modelState.AddModelError(field, message);

        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(),
            modelState);
        return actionContext;
    }

    private static ErrorResponse ExtractBody(IActionResult result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        return Assert.IsType<ErrorResponse>(badRequest.Value);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A single-field error produces a response whose Message equals the one error message.
    /// </summary>
    [Fact]
    public void BuildInvalidModelStateResponse_SingleError_MessageEqualsErrorText()
    {
        var ctx = MakeContext([("Username", "The Username field is required.")]);
        var result = Startup.BuildInvalidModelStateResponse(ctx);

        var body = ExtractBody(result);
        Assert.Equal("InvalidRequest", body.Error);
        Assert.Equal("The Username field is required.", body.Message);
    }

    /// <summary>
    /// Multiple field errors are joined with "; " in the response Message.
    /// </summary>
    [Fact]
    public void BuildInvalidModelStateResponse_MultipleErrors_MessagesJoinedWithSemicolon()
    {
        var ctx = MakeContext([
            ("Username", "The Username field is required."),
            ("Password", "The Password field is required."),
        ]);
        var result = Startup.BuildInvalidModelStateResponse(ctx);

        var body = ExtractBody(result);
        Assert.Equal("InvalidRequest", body.Error);

        // Both messages must be present; order may vary with ModelStateDictionary
        Assert.Contains("The Username field is required.", body.Message);
        Assert.Contains("The Password field is required.", body.Message);
        Assert.Contains("; ", body.Message);
    }

    /// <summary>
    /// An empty ModelState (no errors) falls back to the sentinel message.
    /// </summary>
    [Fact]
    public void BuildInvalidModelStateResponse_EmptyModelState_FallsBackToSentinel()
    {
        var ctx = MakeContext([]);
        var result = Startup.BuildInvalidModelStateResponse(ctx);

        var body = ExtractBody(result);
        Assert.Equal("InvalidRequest", body.Error);
        Assert.Equal("Request body is invalid.", body.Message);
    }

    /// <summary>
    /// The response is always a 400 Bad Request regardless of how many errors are present.
    /// </summary>
    [Fact]
    public void BuildInvalidModelStateResponse_Always_Returns400StatusCode()
    {
        var ctx = MakeContext([("Foo", "Something is wrong.")]);
        var result = Startup.BuildInvalidModelStateResponse(ctx);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    /// <summary>
    /// Three or more errors from different fields are all aggregated into the Message.
    /// </summary>
    [Fact]
    public void BuildInvalidModelStateResponse_ThreeErrors_AllMessagesIncluded()
    {
        var ctx = MakeContext([
            ("Field1", "Error A"),
            ("Field2", "Error B"),
            ("Field3", "Error C"),
        ]);
        var result = Startup.BuildInvalidModelStateResponse(ctx);

        var body = ExtractBody(result);
        Assert.Contains("Error A", body.Message);
        Assert.Contains("Error B", body.Message);
        Assert.Contains("Error C", body.Message);
    }
}
