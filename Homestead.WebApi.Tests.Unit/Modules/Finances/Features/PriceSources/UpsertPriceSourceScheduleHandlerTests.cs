using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.PriceSources;

public class UpsertPriceSourceScheduleHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;

    public UpsertPriceSourceScheduleHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SystemSource_Throws_BusinessRuleException()
    {
        var sut = new UpsertPriceSourceScheduleHandler(_db, _currentUser, new PriceSourceScheduleStore(_db));
        await Assert.ThrowsAsync<BusinessRuleException>(() => sut.Handle(
            new UpsertPriceSourceScheduleCommand
            {
                Source = CommodityPriceSource.Manual,
                CronExpression = "0 6 * * *",
            },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpsertReplacesExistingSchedule()
    {
        var ct = TestContext.Current.CancellationToken;

        // The handler now only stages Remove + Add; the command pipeline's transaction commits. These
        // unit tests bypass the pipeline, so save explicitly after each Handle to mirror that commit —
        // each call is a separate command (and therefore a separate transaction) in production.
        var sut = new UpsertPriceSourceScheduleHandler(_db, _currentUser, new PriceSourceScheduleStore(_db));
        var first = await sut.Handle(new UpsertPriceSourceScheduleCommand
        {
            Source = CommodityPriceSource.BankOfCanada,
            CronExpression = "0 6 * * *",
        }, ct);
        await _db.SaveChangesAsync(ct);

        var second = await sut.Handle(new UpsertPriceSourceScheduleCommand
        {
            Source = CommodityPriceSource.BankOfCanada,
            CronExpression = "0 7 * * *",
        }, ct);
        await _db.SaveChangesAsync(ct);

        var schedules = _db.ProcessingSchedules.ToList();
        var single = Assert.Single(schedules);
        Assert.Equal(second.Id, single.Id);
        Assert.NotEqual(first.Id, single.Id);
        Assert.Equal("0 7 * * *", single.CronExpression);
        Assert.Equal(ProcessingJobType.ImportCommodityPrices, single.JobType);
        var payload = single.GetPayload<ImportCommodityPricesPayload>();
        Assert.NotNull(payload);
        Assert.Equal(CommodityPriceSource.BankOfCanada, payload!.Source);
    }

    [Fact]
    public async Task PersistsCreatedByUserId()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        var sut = new UpsertPriceSourceScheduleHandler(_db, _currentUser, new PriceSourceScheduleStore(_db));
        var response = await sut.Handle(new UpsertPriceSourceScheduleCommand
        {
            Source = CommodityPriceSource.BankOfCanada,
            CronExpression = "0 6 * * *",
        }, ct);
        // The handler stages the Add; the pipeline transaction commits. Bypassing the pipeline here, save
        // explicitly to mirror that commit before asserting on persisted state.
        await _db.SaveChangesAsync(ct);

        var schedule = _db.ProcessingSchedules.Single(s => s.Id == response.Id);
        Assert.Equal(userId, schedule.CreatedByUserId);
    }
}
