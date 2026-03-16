using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
        try
        {
            var json = JObject.FromObject(request);
            DebugSession.TraceStatic($"Launch raw JSON: {json.ToString(Formatting.None)}");

            var program = json["program"]?.ToString() ?? "";
            var stopOnEntry = json["stopOnEntry"]?.Value<bool>() ?? false;
            var cwd = json["cwd"]?.ToString();
            var argsToken = json["args"];
            string[]? args = argsToken != null ? argsToken.ToObject<string[]>() : null;

            var testMode = json["__testMode"]?.Value<bool>() ?? false;
            var testFilter = json["__testFilter"]?.ToString();

            DebugSession.TraceStatic($"Launch extracted: program=\"{program}\", stopOnEntry={stopOnEntry}, cwd=\"{cwd}\", args={args?.Length ?? 0}, testMode={testMode}");

            _session.Launch(program, cwd, stopOnEntry, args, testMode, testFilter);
            DebugSession.TraceStatic("Launch completed");

            return Task.FromResult(new LaunchResponse());
        }
        catch (Exception ex)
        {
            DebugSession.TraceStatic($"Launch FAILED: {ex}");
            throw;
        }
    }
}
