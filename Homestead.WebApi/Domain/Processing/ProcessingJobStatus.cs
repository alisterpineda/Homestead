namespace Homestead.WebApi.Domain.Processing;

public enum ProcessingJobStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    PartiallyCompleted
}
