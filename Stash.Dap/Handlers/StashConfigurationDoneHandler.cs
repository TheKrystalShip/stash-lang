using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashConfigurationDoneHandler : ConfigurationDoneHandlerBase
{
    private readonly DebugSession _session;

    public StashConfigurationDoneHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<ConfigurationDoneResponse> Handle(ConfigurationDoneArguments request, CancellationToken cancellationToken)
    {
        _session.ConfigurationDone();
        return Task.FromResult(new ConfigurationDoneResponse());
    }
}
