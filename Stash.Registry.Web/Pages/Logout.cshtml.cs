using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Web.Auth;

namespace Stash.Registry.Web.Pages;

/// <summary>
/// Logout page — handles <c>POST /logout</c> to terminate the current BFF session.
/// </summary>
/// <remarks>
/// <b>GET /logout</b> — renders a brief "Logging out…" page (handles accidental GET navigation).
/// <b>POST /logout</b> — anti-forgery validated (global filter), delegates to
/// <see cref="LogoutService.LogoutAsync"/>: best-effort revoke of the publish token,
/// session removal, cookie clear, then redirects to <c>/</c>.
/// </remarks>
[AllowAnonymous]
public sealed class LogoutModel : PageModel
{
    private readonly LogoutService _logoutService;

    public LogoutModel(LogoutService logoutService)
    {
        _logoutService = logoutService;
    }

    public void OnGet()
    {
        // Renders the "Logging out…" placeholder for GET navigation.
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _logoutService.LogoutAsync(HttpContext);
        return Redirect("/");
    }
}
