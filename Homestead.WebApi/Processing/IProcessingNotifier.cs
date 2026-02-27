using Homestead.WebApi.Domains.Processing;

namespace Homestead.WebApi.Processing;

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
