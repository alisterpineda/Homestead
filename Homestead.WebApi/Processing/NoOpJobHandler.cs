using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Processing;

public class NoOpJobHandler : IJobHandler
{
    public ProcessingJobType Type => ProcessingJobType.NoOp;

    public Task HandleAsync(ProcessingJob job, CancellationToken ct) => Task.CompletedTask;
}
