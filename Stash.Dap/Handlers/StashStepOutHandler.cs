using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashStepOutHandler : StepOutHandlerBase
{
    private readonly DebugSession _session;

    public StashStepOutHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<StepOutResponse> Handle(StepOutArguments request, CancellationToken cancellationToken)
    {
        _session.StepOut();
        return Task.FromResult(new StepOutResponse());
    }
}
