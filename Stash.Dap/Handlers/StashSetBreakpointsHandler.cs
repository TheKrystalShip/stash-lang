using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashSetBreakpointsHandler : SetBreakpointsHandlerBase
{
    private readonly DebugSession _session;

    public StashSetBreakpointsHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<SetBreakpointsResponse> Handle(SetBreakpointsArguments request, CancellationToken cancellationToken)
    {
        var path = request.Source?.Path ?? "";
        var sourceBreakpoints = request.Breakpoints ?? new Container<SourceBreakpoint>();
        var result = _session.SetBreakpoints(path, sourceBreakpoints);
        return Task.FromResult(new SetBreakpointsResponse
        {
            Breakpoints = new Container<Breakpoint>(result),
        });
    }
}
