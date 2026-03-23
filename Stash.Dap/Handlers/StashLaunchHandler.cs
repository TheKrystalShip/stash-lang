using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Stash.Dap.Handlers;

/// <summary>
/// Handles the DAP <c>launch</c> request to start debugging a Stash script.
/// </summary>
/// <remarks>
/// Extracts launch configuration from the raw JSON request and delegates to
/// <see cref="DebugSession.Launch"/>.
/// </remarks>
public class StashLaunchHandler : LaunchHandlerBase
{
    /// <summary>The debug session that manages all debugging state.</summary>
    private readonly DebugSession _session;

    /// <summary>
    /// Initializes a new instance of the <see cref="StashLaunchHandler"/> class.
    /// </summary>
    /// <param name="session">The <see cref="DebugSession"/> to delegate launch operations to.</param>
    public StashLaunchHandler(DebugSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Processes the DAP <c>launch</c> request and starts the debug session.
    /// </summary>
    /// <param name="request">The launch arguments containing program path and options.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A <see cref="Task{LaunchResponse}"/> representing the launch response.</returns>
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
