using System;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>setVariable</c> request to change the value of a variable at runtime.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.SetVariable"/> and returns the updated value.
/// Errors are caught and returned as part of the response rather than being re-thrown.
/// </remarks>
public class StashSetVariableHandler : SetVariableHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashSetVariableHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to set variables on.</param>
    public StashSetVariableHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>setVariable</c> request and updates the variable's value.
    /// </summary>
    /// <param name="request">The arguments containing the variable reference, name, and new value.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{SetVariableResponse}"/> containing the new value or an error message.</returns>
    public override Task<SetVariableResponse> Handle(SetVariableArguments request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Name))
            {
                throw new InvalidOperationException("Variable name is required.");
            }

            var result = _session.SetVariable(
                (int)request.VariablesReference,
                request.Name,
                request.Value);

            return Task.FromResult(new SetVariableResponse
            {
                Value = result.Value,
                Type = result.Type,
                VariablesReference = result.VariablesReference,
            });
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(new SetVariableResponse
            {
                Value = $"Error: {ex.Message}",
            });
        }
    }
}
