namespace Stash.Scheduler;

using System.Collections.Generic;
using Stash.Scheduler.Models;

public interface IServiceManager
{
    ServiceResult Install(ServiceDefinition definition);
    ServiceResult Uninstall(string serviceName);
    ServiceResult Start(string serviceName);
    ServiceResult Stop(string serviceName);
    ServiceResult Restart(string serviceName);
    ServiceResult Enable(string serviceName);
    ServiceResult Disable(string serviceName);
    ServiceStatus GetStatus(string serviceName);
    IReadOnlyList<ServiceInfo> List();
    IReadOnlyList<ExecutionRecord> GetHistory(string serviceName, int maxRecords = 20);
    bool IsAvailable();
}
