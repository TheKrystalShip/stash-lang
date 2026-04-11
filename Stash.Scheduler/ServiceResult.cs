namespace Stash.Scheduler;

public readonly record struct ServiceResult(bool Success, string? Error = null)
{
    public static ServiceResult Ok() => new(true);
    public static ServiceResult Fail(string error) => new(false, error);
}
