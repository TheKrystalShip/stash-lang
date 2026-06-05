using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Web.Auth;

namespace Stash.Registry.Web.Pages;

/// <summary>
/// Login page — anonymously reachable entry point for the BFF maintainer area.
/// Does NOT carry <c>[Authorize]</c>; the page model is constructed for all callers.
/// </summary>
/// <remarks>
/// <b>GET /login</b> — renders the username/password form with an anti-forgery token
/// (automatic via the global <see cref="Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute"/> filter).
/// <b>POST /login</b> — validates anti-forgery (enforced globally), then delegates to
/// <see cref="LoginService.LoginAsync"/>. On success: cookie set, redirect to returnUrl or /dashboard.
/// On failure: re-renders with error message, no cookie set.
/// </remarks>
[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly LoginService _loginService;

    public LoginModel(LoginService loginService)
    {
        _loginService = loginService;
    }

    /// <summary>The username entered by the user.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Username is required.")]
    public string? Username { get; set; }

    /// <summary>The password entered by the user.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Password is required.")]
    public string? Password { get; set; }

    /// <summary>
    /// Optional return URL from the <c>[Authorize]</c> challenge redirect.
    /// Only honored if it is a same-origin relative path — validated here with
    /// <see cref="Microsoft.AspNetCore.Mvc.IUrlHelper.IsLocalUrl"/> before being passed
    /// to <see cref="LoginService.LoginAsync"/>.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>User-facing error message on failed login. Null when the page is first rendered.</summary>
    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        // Nothing to do — form renders from bound properties.
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        // Validate returnUrl with the framework's IsLocalUrl helper before passing to the
        // service — prevents open-redirect (e.g. /\evil.com passes a simple StartsWith('/')
        // check but is rejected by IsLocalUrl). Pass null if not local; the service falls
        // back to /dashboard.
        var safeReturnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null;

        var result = await _loginService.LoginAsync(
            Username!,
            Password!,
            safeReturnUrl,
            HttpContext);

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            // Clear the password so it is not round-tripped to the browser.
            Password = null;
            return Page();
        }

        return LocalRedirect(result.RedirectUrl!);
    }
}
