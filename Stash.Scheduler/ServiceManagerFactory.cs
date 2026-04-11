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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new LaunchdServiceManager(systemMode);

        throw new PlatformNotSupportedException(
            $"Stash.Scheduler does not yet support {RuntimeInformation.OSDescription}. " +
            "Currently only Linux and macOS are supported.");
    }
}
