using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashSetExceptionBreakpointsHandler : SetExceptionBreakpointsHandlerBase
{
    private readonly DebugSession _session;

    public StashSetExceptionBreakpointsHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<SetExceptionBreakpointsResponse> Handle(SetExceptionBreakpointsArguments request, CancellationToken cancellationToken)
    {
        var filters = request.Filters ?? new Container<string>();
        _session.SetExceptionBreakpoints(filters);
        return Task.FromResult(new SetExceptionBreakpointsResponse());
    }
}
