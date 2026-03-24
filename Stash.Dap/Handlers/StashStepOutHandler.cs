using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>stepOut</c> request to step out of the current function.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.StepOut"/> to resume execution until the current
/// function returns.
/// </remarks>
public class StashStepOutHandler : StepOutHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashStepOutHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to step out on.</param>
    public StashStepOutHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>stepOut</c> request and runs until the current function returns.
    /// </summary>
    /// <param name="request">The step-out arguments, including the thread ID.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{StepOutResponse}"/> representing the response.</returns>
    public override Task<StepOutResponse> Handle(StepOutArguments request, CancellationToken cancellationToken)
    {
        _session.StepOut((int)request.ThreadId);
        return Task.FromResult(new StepOutResponse());
    }
}
