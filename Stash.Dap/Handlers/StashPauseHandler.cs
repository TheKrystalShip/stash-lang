using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>pause</c> request to suspend execution of the debuggee.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.Pause"/> to interrupt running threads.
/// </remarks>
public class StashPauseHandler : PauseHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashPauseHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to pause execution on.</param>
    public StashPauseHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>pause</c> request and suspends the debuggee.
    /// </summary>
    /// <param name="request">The pause arguments, including the thread ID.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{PauseResponse}"/> representing the response.</returns>
    public override Task<PauseResponse> Handle(PauseArguments request, CancellationToken cancellationToken)
    {
        _session.Pause();
        return Task.FromResult(new PauseResponse());
    }
}
