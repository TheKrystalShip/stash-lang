using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashDisconnectHandler : DisconnectHandlerBase
{
    private readonly DebugSession _session;

    public StashDisconnectHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<DisconnectResponse> Handle(DisconnectArguments request, CancellationToken cancellationToken)
    {
        _session.Disconnect();
        return Task.FromResult(new DisconnectResponse());
    }
}
