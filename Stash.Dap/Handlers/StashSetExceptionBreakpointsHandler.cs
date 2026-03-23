using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>setExceptionBreakpoints</c> request to configure exception-based breakpoints.
/// </summary>
/// <remarks>
/// Forwards the filter selection to <see cref="DebugSession.SetExceptionBreakpoints"/> so the
/// debugger knows which exception categories should trigger a break.
/// </remarks>
public class StashSetExceptionBreakpointsHandler : SetExceptionBreakpointsHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashSetExceptionBreakpointsHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to configure exception breakpoints on.</param>
    public StashSetExceptionBreakpointsHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>setExceptionBreakpoints</c> request and applies the selected filters.
    /// </summary>
    /// <param name="request">The arguments containing the exception filter identifiers to enable.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{SetExceptionBreakpointsResponse}"/> representing the response.</returns>
    public override Task<SetExceptionBreakpointsResponse> Handle(SetExceptionBreakpointsArguments request, CancellationToken cancellationToken)
    {
        var filters = request.Filters ?? new Container<string>();
        _session.SetExceptionBreakpoints(filters);
        return Task.FromResult(new SetExceptionBreakpointsResponse());
    }
}
