using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>configurationDone</c> request to signal that all configuration has been sent.
/// </summary>
/// <remarks>
/// Notifies the <see cref="DebugSession"/> that the client has finished sending its initial
/// configuration requests, allowing program execution to begin.
/// </remarks>
public class StashConfigurationDoneHandler : ConfigurationDoneHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashConfigurationDoneHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to notify when configuration is complete.</param>
    public StashConfigurationDoneHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>configurationDone</c> request and signals the session to proceed.
    /// </summary>
    /// <param name="request">The configuration done arguments.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{ConfigurationDoneResponse}"/> representing the response.</returns>
    public override Task<ConfigurationDoneResponse> Handle(ConfigurationDoneArguments request, CancellationToken cancellationToken)
    {
        _session.ConfigurationDone();
        return Task.FromResult(new ConfigurationDoneResponse());
    }
}
