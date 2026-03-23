using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>continue</c> request to resume execution of the debuggee.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.Continue"/> to resume all threads.
/// </remarks>
public class StashContinueHandler : ContinueHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashContinueHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to resume execution on.</param>
    public StashContinueHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>continue</c> request and resumes program execution.
    /// </summary>
    /// <param name="request">The continue arguments, including the thread ID.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{ContinueResponse}"/> indicating all threads have continued.</returns>
    public override Task<ContinueResponse> Handle(ContinueArguments request, CancellationToken cancellationToken)
    {
        _session.Continue();
        return Task.FromResult(new ContinueResponse { AllThreadsContinued = true });
    }
}
