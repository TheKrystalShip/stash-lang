using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>stackTrace</c> request to retrieve the current call stack.
/// </summary>
/// <remarks>
/// Delegates to <see cref="DebugSession.GetStackTrace"/> to obtain the ordered list of
/// stack frames for the suspended thread.
/// </remarks>
public class StashStackTraceHandler : StackTraceHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashStackTraceHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to retrieve the stack trace from.</param>
    public StashStackTraceHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>stackTrace</c> request and returns the current call stack.
    /// </summary>
    /// <param name="request">The stack trace arguments, including the thread ID and optional range.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{StackTraceResponse}"/> containing the stack frames and total frame count.</returns>
    public override Task<StackTraceResponse> Handle(StackTraceArguments request, CancellationToken cancellationToken)
    {
        var frames = _session.GetStackTrace((int)request.ThreadId);
        return Task.FromResult(new StackTraceResponse
        {
            StackFrames = new Container<StackFrame>(frames),
            TotalFrames = frames.Count,
        });
    }
}
