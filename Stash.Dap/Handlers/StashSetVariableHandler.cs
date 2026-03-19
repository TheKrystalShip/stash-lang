using System;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashSetVariableHandler : SetVariableHandlerBase
{
    private readonly DebugSession _session;

    public StashSetVariableHandler(DebugSession session)
    {
        _session = session;
    }

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
