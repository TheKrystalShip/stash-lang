using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Stash.Tests.Registry.Web.Fixtures;

/// <summary>
/// Fail-path fixtures for <see cref="Stash.Tests.Registry.Web.AntiForgeryConstructMetaTests"/>.
/// Proves the anti-forgery guard has teeth by providing:
/// <list type="bullet">
///   <item>A known-bad page-conventions list (without the global CSRF attribute) for the
///   structural secondary check.</item>
///   <item>A known-good page-conventions list (with the global CSRF attribute) for the
///   structural happy path.</item>
/// </list>
/// The <b>load-bearing</b> behavioral assertion (POST without a token → 400) is driven
/// by the production <see cref="Microsoft.AspNetCore.TestHost.WebApplicationFactory{TEntryPoint}"/>
/// directly in <see cref="Stash.Tests.Registry.Web.AntiForgeryConstructMetaTests"/>.
/// </summary>
public static class AntiForgeryFailPathFixture
{
    /// <summary>
    /// A filter list that does NOT contain <see cref="AutoValidateAntiforgeryTokenAttribute"/>.
    /// Used to prove the structural scanner correctly reports the attribute as absent.
    /// </summary>
    public static readonly IFilterMetadata[] FiltersWithoutCsrfGuard = [];

    /// <summary>
    /// A filter list that DOES contain <see cref="AutoValidateAntiforgeryTokenAttribute"/>.
    /// Used to prove the structural scanner correctly reports the attribute as present.
    /// </summary>
    public static readonly IFilterMetadata[] FiltersWithCsrfGuard =
    [
        new AutoValidateAntiforgeryTokenAttribute(),
    ];
}
