using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashVariablesHandler : VariablesHandlerBase
{
    private readonly DebugSession _session;

    public StashVariablesHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<VariablesResponse> Handle(VariablesArguments request, CancellationToken cancellationToken)
    {
        var variables = _session.GetVariables((int)request.VariablesReference);
        return Task.FromResult(new VariablesResponse
        {
            Variables = new Container<Variable>(variables),
        });
    }
}
