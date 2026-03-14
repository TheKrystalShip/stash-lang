using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using DapThread = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread;

namespace Stash.Dap.Handlers;

public class StashThreadsHandler : ThreadsHandlerBase
{
    private readonly DebugSession _session;

    public StashThreadsHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<ThreadsResponse> Handle(ThreadsArguments request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ThreadsResponse
        {
            Threads = new Container<DapThread>(new DapThread { Id = 1, Name = "Main Thread" }),
        });
    }
}
