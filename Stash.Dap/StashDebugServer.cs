namespace Stash.Dap;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Server;
using Stash.Dap.Handlers;

public static class StashDebugServer
{
    public static async Task RunAsync()
    {
        var session = new DebugSession();

        var server = await DebugAdapterServer.From(options =>
        {
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Warning);
                })
                .WithServices(services =>
                {
                    services.AddSingleton(session);
                })
                .OnInitialized((server, request, response, ct) =>
                {
                    DebugSession.TraceStatic("OnInitialized callback fired");
                    session.SetServer(server);
                    DebugSession.TraceStatic("OnInitialized: server reference set");
                    return Task.CompletedTask;
                })
                .WithHandler<StashLaunchHandler>()
                .WithHandler<StashConfigurationDoneHandler>()
                .WithHandler<StashDisconnectHandler>()
                .WithHandler<StashSetBreakpointsHandler>()
                .WithHandler<StashSetExceptionBreakpointsHandler>()
                .WithHandler<StashThreadsHandler>()
                .WithHandler<StashContinueHandler>()
                .WithHandler<StashNextHandler>()
                .WithHandler<StashStepInHandler>()
                .WithHandler<StashStepOutHandler>()
                .WithHandler<StashPauseHandler>()
                .WithHandler<StashStackTraceHandler>()
                .WithHandler<StashScopesHandler>()
                .WithHandler<StashVariablesHandler>()
                .WithHandler<StashEvaluateHandler>()
                .WithHandler<StashSetVariableHandler>()
                .WithHandler<StashSetFunctionBreakpointsHandler>()
                .WithHandler<StashLoadedSourcesHandler>();

            // Set capabilities for the framework's built-in initialize handler.
            // Capabilities like SupportsConfigurationDoneRequest are auto-detected
            // from registered handlers (e.g., StashConfigurationDoneHandler).
            options.Capabilities.SupportsConditionalBreakpoints = true;
            options.Capabilities.SupportsHitConditionalBreakpoints = true;
            options.Capabilities.SupportsEvaluateForHovers = true;
            options.Capabilities.SupportsLogPoints = true;
            options.Capabilities.SupportsSetVariable = true;
            options.Capabilities.SupportsFunctionBreakpoints = true;
            options.Capabilities.SupportsLoadedSourcesRequest = true;
            options.Capabilities.ExceptionBreakpointFilters = new Container<ExceptionBreakpointsFilter>(
                new ExceptionBreakpointsFilter { Filter = "all", Label = "All Exceptions", Default = false },
                new ExceptionBreakpointsFilter { Filter = "uncaught", Label = "Uncaught Exceptions", Default = true }
            );
        }).ConfigureAwait(false);

        await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
    }
}
