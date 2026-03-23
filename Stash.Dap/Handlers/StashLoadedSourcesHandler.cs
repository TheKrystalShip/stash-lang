using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>loadedSources</c> request to enumerate all source files loaded by the debuggee.
/// </summary>
/// <remarks>
/// Retrieves the list of loaded source files from <see cref="DebugSession.GetLoadedSources"/>.
/// </remarks>
public class StashLoadedSourcesHandler : LoadedSourcesHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashLoadedSourcesHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to retrieve loaded sources from.</param>
    public StashLoadedSourcesHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>loadedSources</c> request and returns all currently loaded source files.
    /// </summary>
    /// <param name="request">The loaded sources arguments.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{LoadedSourcesResponse}"/> containing the collection of loaded sources.</returns>
    public override Task<LoadedSourcesResponse> Handle(LoadedSourcesArguments request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new LoadedSourcesResponse
        {
            Sources = new Container<Source>(_session.GetLoadedSources()),
        });
    }
}
