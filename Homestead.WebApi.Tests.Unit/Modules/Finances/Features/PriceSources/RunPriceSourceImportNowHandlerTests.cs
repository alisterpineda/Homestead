using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.RunPriceSourceImportNow;
using Homestead.WebApi.Processing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.PriceSources;

public class RunPriceSourceImportNowHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IJobQueue _queue;
    private readonly ICurrentUserAccessor _currentUser;

    public RunPriceSourceImportNowHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _queue = Substitute.For<IJobQueue>();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SystemSource_Throws()
    {
        var sut = new RunPriceSourceImportNowHandler(_db, _queue, _currentUser);
        await Assert.ThrowsAsync<BusinessRuleException>(() => sut.Handle(
            new RunPriceSourceImportNowCommand { Source = CommodityPriceSource.Manual },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisabledSource_Throws()
    {
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = false,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new RunPriceSourceImportNowHandler(_db, _queue, _currentUser);
        await Assert.ThrowsAsync<BusinessRuleException>(() => sut.Handle(
            new RunPriceSourceImportNowCommand { Source = CommodityPriceSource.BankOfCanada },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EnabledSource_EnqueuesJobWithCreator()
    {
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

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

        var sut = new RunPriceSourceImportNowHandler(_db, _queue, _currentUser);
        var response = await sut.Handle(
            new RunPriceSourceImportNowCommand { Source = CommodityPriceSource.BankOfCanada },
            TestContext.Current.CancellationToken);

        Assert.Equal(jobId, response.JobId);
        Assert.NotNull(captured);
        Assert.Equal(userId, captured!.CreatedByUserId);
        Assert.NotNull(captured.Payload);
    }
}
