using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashContinueHandler : ContinueHandlerBase
{
    private readonly DebugSession _session;

    public StashContinueHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<ContinueResponse> Handle(ContinueArguments request, CancellationToken cancellationToken)
    {
        _session.Continue();
        return Task.FromResult(new ContinueResponse { AllThreadsContinued = true });
    }
}
