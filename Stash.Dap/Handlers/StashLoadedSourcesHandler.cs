using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashLoadedSourcesHandler : LoadedSourcesHandlerBase
{
    private readonly DebugSession _session;

    public StashLoadedSourcesHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<LoadedSourcesResponse> Handle(LoadedSourcesArguments request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new LoadedSourcesResponse
        {
            Sources = new Container<Source>(_session.GetLoadedSources()),
        });
    }
}
