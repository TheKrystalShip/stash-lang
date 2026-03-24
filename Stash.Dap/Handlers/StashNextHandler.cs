using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>next</c> request to step over the current line.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.Next"/> to advance execution by one statement
/// without stepping into function calls.
/// </remarks>
public class StashNextHandler : NextHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashNextHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to step on.</param>
    public StashNextHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>next</c> request and steps over the current line.
    /// </summary>
    /// <param name="request">The next arguments, including the thread ID.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{NextResponse}"/> representing the response.</returns>
    public override Task<NextResponse> Handle(NextArguments request, CancellationToken cancellationToken)
    {
        _session.Next((int)request.ThreadId);
        return Task.FromResult(new NextResponse());
    }
}
