using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashStackTraceHandler : StackTraceHandlerBase
{
    private readonly DebugSession _session;

    public StashStackTraceHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<StackTraceResponse> Handle(StackTraceArguments request, CancellationToken cancellationToken)
    {
        var frames = _session.GetStackTrace();
        return Task.FromResult(new StackTraceResponse
        {
            StackFrames = new Container<StackFrame>(frames),
            TotalFrames = frames.Count,
        });
    }
}
