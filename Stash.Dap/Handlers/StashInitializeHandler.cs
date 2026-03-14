using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashInitializeHandler : DebugAdapterInitializeHandlerBase
{
    private readonly DebugSession _session;

    public StashInitializeHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<InitializeResponse> Handle(InitializeRequestArguments request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new InitializeResponse
        {
            SupportsConfigurationDoneRequest = true,
            SupportsEvaluateForHovers = true,
            SupportsConditionalBreakpoints = true,
            SupportsHitConditionalBreakpoints = true,
            SupportsLogPoints = true,
            SupportsSetVariable = false,
            SupportsFunctionBreakpoints = false,
            SupportsExceptionInfoRequest = false,
            SupportsLoadedSourcesRequest = false,
            ExceptionBreakpointFilters = new Container<ExceptionBreakpointsFilter>(
                new ExceptionBreakpointsFilter { Filter = "all", Label = "All Exceptions", Default = false },
                new ExceptionBreakpointsFilter { Filter = "uncaught", Label = "Uncaught Exceptions", Default = true }
            ),
        });
    }
}
