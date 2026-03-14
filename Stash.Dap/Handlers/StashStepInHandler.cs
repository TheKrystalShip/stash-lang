using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashStepInHandler : StepInHandlerBase
{
    private readonly DebugSession _session;

    public StashStepInHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<StepInResponse> Handle(StepInArguments request, CancellationToken cancellationToken)
    {
        _session.StepIn();
        return Task.FromResult(new StepInResponse());
    }
}
