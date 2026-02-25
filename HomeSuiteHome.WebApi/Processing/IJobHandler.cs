using HomeSuiteHome.WebApi.Domains.Processing;

namespace HomeSuiteHome.WebApi.Processing;

public interface IJobHandler
{
    ProcessingJobType Type { get; }
    Task HandleAsync(ProcessingJob job, CancellationToken ct);
}
