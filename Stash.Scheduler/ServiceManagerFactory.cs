namespace Stash.Scheduler;

using System;
using System.Runtime.InteropServices;
using Stash.Scheduler.Platforms;

public static class ServiceManagerFactory
{
    public static IServiceManager Create(bool systemMode = false)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new SystemdServiceManager(systemMode);

        // Phase 1: only Linux/systemd backend
        // Future phases add: LaunchdServiceManager (macOS), WindowsTaskServiceManager (Windows)
        throw new PlatformNotSupportedException(
            $"Stash.Scheduler does not yet support {RuntimeInformation.OSDescription}. " +
            "Currently only Linux (systemd) is supported.");
    }
}
