using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashPauseHandler : PauseHandlerBase
{
    private readonly DebugSession _session;

    public StashPauseHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<PauseResponse> Handle(PauseArguments request, CancellationToken cancellationToken)
    {
        _session.Pause();
        return Task.FromResult(new PauseResponse());
    }
}
