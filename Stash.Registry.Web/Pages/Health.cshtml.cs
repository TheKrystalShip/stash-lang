using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Services;

namespace Stash.Registry.Web.Pages;

/// <summary>
/// Page model for <c>/health</c>. Calls <see cref="IRegistryClient.GetDiscoveryAsync"/> and
/// renders a subset of <see cref="DiscoveryResponse"/> fields to confirm the DI wiring works end-to-end.
/// </summary>
public sealed class HealthModel : PageModel
{
    private readonly IRegistryClient _registryClient;

    public HealthModel(IRegistryClient registryClient)
    {
        _registryClient = registryClient;
    }

    /// <summary>The discovery response from the registry, or <c>null</c> if the registry is unreachable.</summary>
    public DiscoveryResponse? Discovery { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Discovery = await _registryClient.GetDiscoveryAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Registry unreachable — Discovery stays null; the view renders the "unreachable" message.
            Discovery = null;
        }
    }
}
