using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Stash.Registry.Web.Pages;

/// <summary>
/// Page model for the production error page (<c>/Error</c>).
/// Registered via <c>app.UseExceptionHandler("/Error")</c> for non-Development environments.
/// </summary>
/// <remarks>
/// This page is only reached when an unhandled exception bubbles past all page-model catch blocks.
/// It renders a generic "something went wrong" message — no stack trace, no exception details.
/// </remarks>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    /// <summary>
    /// The ASP.NET request-id, or <c>null</c> if not available.
    /// Useful for correlating with server logs without leaking exception details.
    /// </summary>
    public string? RequestId { get; private set; }

    public void OnGet()
    {
        RequestId = HttpContext.TraceIdentifier;
    }
}
