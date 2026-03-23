using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>variables</c> request to retrieve child variables for a variable reference.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.GetVariables"/> to expand a structured variable or scope
/// into its member variables.
/// </remarks>
public class StashVariablesHandler : VariablesHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashVariablesHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to retrieve variable data from.</param>
    public StashVariablesHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>variables</c> request and returns the child variables for a given reference.
    /// </summary>
    /// <param name="request">The arguments containing the variables reference to expand.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{VariablesResponse}"/> containing the collection of variables.</returns>
    public override Task<VariablesResponse> Handle(VariablesArguments request, CancellationToken cancellationToken)
    {
        var variables = _session.GetVariables((int)request.VariablesReference);
        return Task.FromResult(new VariablesResponse
        {
            Variables = new Container<Variable>(variables),
        });
    }
}
