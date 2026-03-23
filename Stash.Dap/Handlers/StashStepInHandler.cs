using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>stepIn</c> request to step into the next function call.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.StepIn"/> to advance execution into a called function.
/// </remarks>
public class StashStepInHandler : StepInHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashStepInHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to step into on.</param>
    public StashStepInHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>stepIn</c> request and steps into the next call.
    /// </summary>
    /// <param name="request">The step-in arguments, including the thread ID.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{StepInResponse}"/> representing the response.</returns>
    public override Task<StepInResponse> Handle(StepInArguments request, CancellationToken cancellationToken)
    {
        _session.StepIn();
        return Task.FromResult(new StepInResponse());
    }
}
