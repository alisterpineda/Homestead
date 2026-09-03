using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.RunPriceSourceImportNow;
using Homestead.WebApi.Processing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.PriceSources;

public class RunPriceSourceImportNowHandlerTests
{
    private readonly IJobQueue _queue;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly StubPriceSourceRegistry _sources;

    public RunPriceSourceImportNowHandlerTests()
    {
        _queue = Substitute.For<IJobQueue>();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _sources = new StubPriceSourceRegistry();
    }

    private RunPriceSourceImportNowHandler Sut() => new(_queue, _currentUser, _sources);

    [Fact]
    public async Task SystemSource_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().Handle(
            new RunPriceSourceImportNowCommand { Source = CommodityPriceSource.Manual },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnregisteredSource_Throws()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().Handle(
            new RunPriceSourceImportNowCommand { Source = CommodityPriceSource.BankOfCanada },
            TestContext.Current.CancellationToken));
        Assert.Contains("No provider is registered", ex.Message);
    }

    [Fact]
    public async Task DisabledSource_Throws()
    {
        _sources.Providers[CommodityPriceSource.BankOfCanada] =
            TestPriceSources.Provider(CommodityPriceSource.BankOfCanada, enabled: false, configured: false);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().Handle(
            new RunPriceSourceImportNowCommand { Source = CommodityPriceSource.BankOfCanada },
            TestContext.Current.CancellationToken));
        Assert.Contains("is disabled", ex.Message);
    }

    [Fact]
    public async Task NotConfiguredSource_Throws_WithReason()
    {
        _sources.Providers[CommodityPriceSource.AlphaVantage] = TestPriceSources.Provider(
            CommodityPriceSource.AlphaVantage, enabled: true, configured: false, error: "ApiKey is required.");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().Handle(
            new RunPriceSourceImportNowCommand { Source = CommodityPriceSource.AlphaVantage },
            TestContext.Current.CancellationToken));
        Assert.Contains("is not configured: ApiKey is required.", ex.Message);
    }

    [Fact]
    public async Task EnabledSource_EnqueuesJobWithCreator()
    {
        _sources.Providers[CommodityPriceSource.BankOfCanada] =
            TestPriceSources.Provider(CommodityPriceSource.BankOfCanada);

        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);

        var jobId = Guid.NewGuid();
        _queue
            .EnqueueAsync(
                ProcessingJobType.ImportCommodityPrices,
                Arg.Any<Action<EnqueueJobRequest>?>(),
                Arg.Any<CancellationToken>())
            .Returns(jobId);

        EnqueueJobRequest? captured = null;
        _queue
            .When(q => q.EnqueueAsync(
                ProcessingJobType.ImportCommodityPrices,
                Arg.Any<Action<EnqueueJobRequest>?>(),
                Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var configure = (Action<EnqueueJobRequest>?)call[1];
                captured = new EnqueueJobRequest();
                configure?.Invoke(captured);
            });

        var response = await Sut().Handle(
            new RunPriceSourceImportNowCommand { Source = CommodityPriceSource.BankOfCanada },
            TestContext.Current.CancellationToken);

        Assert.Equal(jobId, response.JobId);
        Assert.NotNull(captured);
        Assert.Equal(userId, captured!.CreatedByUserId);
        Assert.NotNull(captured.Payload);
    }
}
