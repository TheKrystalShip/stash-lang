using System;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashEvaluateHandler : EvaluateHandlerBase
{
    private readonly DebugSession _session;

    public StashEvaluateHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<EvaluateResponse> Handle(EvaluateArguments request, CancellationToken cancellationToken)
    {
        try
        {
            var result = _session.Evaluate(request.Expression, (int?)request.FrameId);
            return Task.FromResult(new EvaluateResponse
            {
                Result = result,
                VariablesReference = 0,
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new EvaluateResponse
            {
                Result = $"Error: {ex.Message}",
                VariablesReference = 0,
            });
        }
    }
}
