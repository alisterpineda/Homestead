using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.PriceSources;

public class PatchPriceSourceHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;

    public PatchPriceSourceHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));

        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private PatchPriceSourceHandler Sut() =>
        new(_db, _currentUser, _audit, _unitOfWork);

    [Fact]
    public async Task SystemSource_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().HandleAsync(
            CommodityPriceSource.Manual, new PatchPriceSourceBody(),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PairAllowList_UnknownSymbol_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().HandleAsync(
            CommodityPriceSource.BankOfCanada,
            new PatchPriceSourceBody { PairAllowList = new[] { "ZZZ" } },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PairAllowList_SecuritySymbol_Accepted()
    {
        _db.Commodities.Add(new Commodity
        {
            Id = Guid.NewGuid(),
            Symbol = "VEQT",
            Name = "Vanguard All-Equity ETF",
            Kind = CommodityKind.Security,
            Exchange = "TSX",
            DisplayPrecision = 4,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Sut().HandleAsync(
            CommodityPriceSource.BankOfCanada,
            new PatchPriceSourceBody { PairAllowList = new[] { "VEQT" } },
            TestContext.Current.CancellationToken);

        Assert.Contains("VEQT", _db.CommodityPriceSourceSettings.Single().PairAllowList);
    }

    [Fact]
    public async Task PairAllowList_Response_ReturnsNormalisedList()
    {
        _db.Commodities.Add(new Commodity
        {
            Id = Guid.NewGuid(),
            Symbol = "USD",
            Name = "US Dollar",
            Kind = CommodityKind.Currency,
            DisplayPrecision = 2,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var response = await Sut().HandleAsync(
            CommodityPriceSource.BankOfCanada,
            new PatchPriceSourceBody { PairAllowList = new[] { " USD ", "usd", "" } },
            TestContext.Current.CancellationToken);

        Assert.Equal(CommodityPriceSource.BankOfCanada, response.Source);
        Assert.Equal(new[] { "USD" }, response.PairAllowList);
        Assert.Contains("USD", _db.CommodityPriceSourceSettings.Single().PairAllowList);
    }

    [Fact]
    public async Task NoPairAllowList_Response_ReturnsEmptyList()
    {
        var response = await Sut().HandleAsync(
            CommodityPriceSource.BankOfCanada, new PatchPriceSourceBody(),
            TestContext.Current.CancellationToken);

        Assert.Empty(response.PairAllowList);
    }
}
