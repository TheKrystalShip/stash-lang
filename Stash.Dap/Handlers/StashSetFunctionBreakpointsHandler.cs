using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashSetFunctionBreakpointsHandler : SetFunctionBreakpointsHandlerBase
{
    private readonly DebugSession _session;

    public StashSetFunctionBreakpointsHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<SetFunctionBreakpointsResponse> Handle(SetFunctionBreakpointsArguments request, CancellationToken cancellationToken)
    {
        var breakpoints = request.Breakpoints ?? new Container<FunctionBreakpoint>();
        var result = _session.SetFunctionBreakpoints(breakpoints);
        return Task.FromResult(new SetFunctionBreakpointsResponse
        {
            Breakpoints = new Container<Breakpoint>(result),
        });
    }
}
