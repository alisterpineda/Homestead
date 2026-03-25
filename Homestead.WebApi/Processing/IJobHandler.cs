using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Processing;

public interface IJobHandler
{
    ProcessingJobType Type { get; }
    Task HandleAsync(ProcessingJob job, CancellationToken ct);
}
