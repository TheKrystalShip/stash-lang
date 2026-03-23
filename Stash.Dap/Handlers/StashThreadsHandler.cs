using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using DapThread = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>threads</c> request to enumerate all active threads in the debuggee.
/// </summary>
/// <remarks>
/// The Stash runtime is single-threaded, so this handler always returns a single main thread.
/// </remarks>
public class StashThreadsHandler : ThreadsHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashThreadsHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to retrieve thread information from.</param>
    public StashThreadsHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>threads</c> request and returns the list of active threads.
    /// </summary>
    /// <param name="request">The threads arguments.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{ThreadsResponse}"/> containing the single main thread.</returns>
    public override Task<ThreadsResponse> Handle(ThreadsArguments request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ThreadsResponse
        {
            Threads = new Container<DapThread>(new DapThread { Id = 1, Name = "Main Thread" }),
        });
    }
}
