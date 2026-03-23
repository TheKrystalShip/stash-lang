using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>setBreakpoints</c> request to configure source breakpoints.
/// </summary>
/// <remarks>
/// Forwards the breakpoint list for a given source file to <see cref="DebugSession.SetBreakpoints"/>
/// and returns the verified breakpoints.
/// </remarks>
public class StashSetBreakpointsHandler : SetBreakpointsHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashSetBreakpointsHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to delegate breakpoint operations to.</param>
    public StashSetBreakpointsHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>setBreakpoints</c> request and returns verified breakpoint information.
    /// </summary>
    /// <param name="request">The arguments containing the source file and desired breakpoints.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{SetBreakpointsResponse}"/> containing the verified breakpoints.</returns>
    public override Task<SetBreakpointsResponse> Handle(SetBreakpointsArguments request, CancellationToken cancellationToken)
    {
        var path = request.Source?.Path ?? "";
        var sourceBreakpoints = request.Breakpoints ?? new Container<SourceBreakpoint>();
        var result = _session.SetBreakpoints(path, sourceBreakpoints);
        return Task.FromResult(new SetBreakpointsResponse
        {
            Breakpoints = new Container<Breakpoint>(result),
        });
    }
}
