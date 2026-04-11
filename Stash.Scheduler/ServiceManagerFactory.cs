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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsTaskServiceManager(systemMode);

        throw new PlatformNotSupportedException(
            $"Stash.Scheduler does not yet support {RuntimeInformation.OSDescription}. " +
            "Currently Linux, macOS, and Windows are supported.");
    }
}
