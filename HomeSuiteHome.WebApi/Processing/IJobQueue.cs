using HomeSuiteHome.WebApi.Domains.Processing;

namespace HomeSuiteHome.WebApi.Processing;

public interface IJobQueue
{
    Task<Guid> EnqueueAsync(ProcessingJobType type, Action<EnqueueJobRequest>? configure = null,
        CancellationToken ct = default);
}
