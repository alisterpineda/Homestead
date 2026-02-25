using HomeSuiteHome.WebApi.Domains.Processing;

namespace HomeSuiteHome.WebApi.Processing;

public interface IProcessingNotifier
{
    Task NotifyJobStatusAsync(
        Guid jobId,
        ProcessingJobStatus status,
        ProcessingEntityType? entityType,
        Guid? entityId,
        string? documentName,
        string? error,
        Guid? userId,
        CancellationToken ct);
}
