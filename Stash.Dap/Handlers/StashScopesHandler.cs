using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashScopesHandler : ScopesHandlerBase
{
    private readonly DebugSession _session;

    public StashScopesHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<ScopesResponse> Handle(ScopesArguments request, CancellationToken cancellationToken)
    {
        var scopes = _session.GetScopes((int)request.FrameId);
        return Task.FromResult(new ScopesResponse
        {
            Scopes = new Container<Scope>(scopes),
        });
    }
}
