using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>setFunctionBreakpoints</c> request to configure function-name breakpoints.
/// </summary>
/// <remarks>
/// Forwards the function breakpoint list to <see cref="DebugSession.SetFunctionBreakpoints"/> and
/// returns the verified breakpoints.
/// </remarks>
public class StashSetFunctionBreakpointsHandler : SetFunctionBreakpointsHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashSetFunctionBreakpointsHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to configure function breakpoints on.</param>
    public StashSetFunctionBreakpointsHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>setFunctionBreakpoints</c> request and returns verified breakpoint information.
    /// </summary>
    /// <param name="request">The arguments containing the function breakpoints to set.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{SetFunctionBreakpointsResponse}"/> containing the verified breakpoints.</returns>
    public override Task<SetFunctionBreakpointsResponse> Handle(SetFunctionBreakpointsArguments request, CancellationToken cancellationToken)
    {
        var breakpoints = request.Breakpoints ?? new Container<FunctionBreakpoint>();
        var result = _session.SetFunctionBreakpoints(breakpoints);
        return Task.FromResult(new SetFunctionBreakpointsResponse
        {
            Breakpoints = new Container<Breakpoint>(result),
        });
    }
}
