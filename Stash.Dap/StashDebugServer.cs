namespace Stash.Dap;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.DebugAdapter.Server;
using Stash.Dap.Handlers;

public static class StashDebugServer
{
    public static async Task RunAsync()
    {
        var server = await DebugAdapterServer.From(options =>
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Warning);
                })
                .WithServices(services =>
                {
                    services.AddSingleton<DebugSession>();
                })
                .WithHandler<StashInitializeHandler>()
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
        ).ConfigureAwait(false);

        var session = server.GetRequiredService<DebugSession>();
        session.SetServer(server);

        await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
    }
}
