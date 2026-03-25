using Homestead.WebApi.Domain.Processing;

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
        CancellationToken ct,
        string? errorCode = null,
        Dictionary<string, string>? errorData = null);
}
