using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashNextHandler : NextHandlerBase
{
    private readonly DebugSession _session;

    public StashNextHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<NextResponse> Handle(NextArguments request, CancellationToken cancellationToken)
    {
        _session.Next();
        return Task.FromResult(new NextResponse());
    }
}
