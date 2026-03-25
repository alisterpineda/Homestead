using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Processing;

public interface IJobQueue
{
    Task<Guid> EnqueueAsync(ProcessingJobType type, Action<EnqueueJobRequest>? configure = null,
        CancellationToken ct = default);
}
