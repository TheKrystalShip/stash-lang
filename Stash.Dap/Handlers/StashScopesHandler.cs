using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>scopes</c> request to retrieve variable scopes for a stack frame.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.GetScopes"/> to retrieve locals, globals, and other
/// scopes available at the specified frame.
/// </remarks>
public class StashScopesHandler : ScopesHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashScopesHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to retrieve scope information from.</param>
    public StashScopesHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>scopes</c> request and returns variable scopes for the given frame.
    /// </summary>
    /// <param name="request">The scopes arguments, including the frame ID.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{ScopesResponse}"/> containing the available scopes.</returns>
    public override Task<ScopesResponse> Handle(ScopesArguments request, CancellationToken cancellationToken)
    {
        var scopes = _session.GetScopes((int)request.FrameId);
        return Task.FromResult(new ScopesResponse
        {
            Scopes = new Container<Scope>(scopes),
        });
    }
}
