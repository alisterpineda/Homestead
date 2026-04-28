using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class NoOpJobHandlerTests
{
    private readonly NoOpJobHandler _sut = new();

    [Fact]
    public void Type_IsNoOp()
    {
        Assert.Equal(ProcessingJobType.NoOp, _sut.Type);
    }

    [Fact]
    public async Task HandleAsync_Completes()
    {
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.NoOp,
            Status = ProcessingJobStatus.InProgress,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);
    }
}
