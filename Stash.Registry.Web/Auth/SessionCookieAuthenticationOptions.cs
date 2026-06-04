using Microsoft.AspNetCore.Authentication;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Options for <see cref="SessionCookieAuthenticationHandler"/>.
/// No additional configuration is required beyond the defaults provided by
/// <see cref="AuthenticationSchemeOptions"/>; this class exists to satisfy the
/// <c>TOptions</c> type parameter of <see cref="Microsoft.AspNetCore.Authentication.AuthenticationHandler{TOptions}"/>.
/// </summary>
public sealed class SessionCookieAuthenticationOptions : AuthenticationSchemeOptions
{
}
