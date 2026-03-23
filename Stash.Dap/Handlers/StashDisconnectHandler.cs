using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>disconnect</c> request to terminate the debug session.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.Disconnect"/> to clean up session resources.
/// </remarks>
public class StashDisconnectHandler : DisconnectHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashDisconnectHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to disconnect.</param>
    public StashDisconnectHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>disconnect</c> request and tears down the debug session.
    /// </summary>
    /// <param name="request">The disconnect arguments, including whether to terminate the debuggee.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{DisconnectResponse}"/> representing the response.</returns>
    public override Task<DisconnectResponse> Handle(DisconnectArguments request, CancellationToken cancellationToken)
    {
        _session.Disconnect();
        return Task.FromResult(new DisconnectResponse());
    }
}
