using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

public class StashLaunchHandler : LaunchHandlerBase
{
    private readonly DebugSession _session;

    public StashLaunchHandler(DebugSession session)
    {
        _session = session;
    }

    public override Task<LaunchResponse> Handle(LaunchRequestArguments request, CancellationToken cancellationToken)
    {
        var json = JObject.FromObject(request);
        var program = json["program"]?.ToString() ?? "";
        var stopOnEntry = json["stopOnEntry"]?.Value<bool>() ?? false;
        var cwd = json["cwd"]?.ToString();
        var argsToken = json["args"];
        string[]? args = argsToken != null ? argsToken.ToObject<string[]>() : null;

        _session.Launch(program, cwd, stopOnEntry, args);
        _session.SendInitialized();

        return Task.FromResult(new LaunchResponse());
    }
}
