using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class CreateProcessingScheduleCommandValidatorTests
{
    private readonly CreateProcessingScheduleCommandValidator _sut = new();

    private static CreateProcessingScheduleCommand Valid() => new()
    {
        Name = "Smoke",
        Description = null,
        JobType = ProcessingJobType.NoOp,
        CronExpression = "*/5 * * * *",
        Priority = 0,
        IsEnabled = true,
    };

    [Fact]
    public async Task Valid_NoOpSchedule_IsValid()
    {
        var result = await _sut.ValidateAsync(Valid(), TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhitespaceName_IsInvalid(string? name)
    {
        var cmd = Valid() with { Name = name! };
        var result = await _sut.ValidateAsync(cmd, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProcessingScheduleCommand.Name));
    }

    [Fact]
    public async Task NameTooLong_IsInvalid()
    {
        var cmd = Valid() with { Name = new string('x', 257) };
        var result = await _sut.ValidateAsync(cmd, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProcessingScheduleCommand.Name));
    }

    [Fact]
    public async Task DescriptionTooLong_IsInvalid()
    {
        var cmd = Valid() with { Description = new string('x', 1025) };
        var result = await _sut.ValidateAsync(cmd, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProcessingScheduleCommand.Description));
    }

    [Theory]
    [InlineData("not a cron")]
    [InlineData("99 99 99 99 99")]
    [InlineData("")]
    public async Task InvalidCron_IsInvalid(string cron)
    {
        var cmd = Valid() with { CronExpression = cron };
        var result = await _sut.ValidateAsync(cmd, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProcessingScheduleCommand.CronExpression));
    }

    [Theory]
    [InlineData(ProcessingJobType.UploadDocument)]
    [InlineData(ProcessingJobType.DownloadDocuments)]
    [InlineData(ProcessingJobType.BulkReprocessDocuments)]
    public async Task NonAllowedJobType_IsInvalid(ProcessingJobType jobType)
    {
        var cmd = Valid() with { JobType = jobType };
        var result = await _sut.ValidateAsync(cmd, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProcessingScheduleCommand.JobType));
    }

    [Fact]
    public async Task ImportCommodityPrices_FromAdminEndpoint_IsRejected()
    {
        var cmd = Valid() with { JobType = ProcessingJobType.ImportCommodityPrices };
        var result = await _sut.ValidateAsync(cmd, TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(CreateProcessingScheduleCommand.JobType)
            && e.ErrorMessage.Contains("price-sources"));
    }
}
