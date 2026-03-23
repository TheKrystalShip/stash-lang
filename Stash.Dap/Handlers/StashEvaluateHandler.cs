using System;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>evaluate</c> request to evaluate an expression in the debuggee.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.Evaluate"/> and returns the result as a string.
/// Errors are caught and returned as part of the response rather than being re-thrown.
/// </remarks>
public class StashEvaluateHandler : EvaluateHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashEvaluateHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to evaluate expressions against.</param>
    public StashEvaluateHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>evaluate</c> request and returns the expression result.
    /// </summary>
    /// <param name="request">The evaluate arguments, including the expression and optional frame ID.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{EvaluateResponse}"/> containing the evaluated result or an error message.</returns>
    public override Task<EvaluateResponse> Handle(EvaluateArguments request, CancellationToken cancellationToken)
    {
        try
        {
            var result = _session.Evaluate(request.Expression, (int?)request.FrameId);
            return Task.FromResult(new EvaluateResponse
            {
                Result = result,
                VariablesReference = 0,
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new EvaluateResponse
            {
                Result = $"Error: {ex.Message}",
                VariablesReference = 0,
            });
        }
    }
}
