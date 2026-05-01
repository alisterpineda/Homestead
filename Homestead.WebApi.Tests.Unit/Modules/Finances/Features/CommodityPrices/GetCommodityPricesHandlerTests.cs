using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.CommodityPrices.GetCommodityPrices;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.CommodityPrices;

public class GetCommodityPricesHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetCommodityPricesHandler _sut;
    private readonly Guid _commodityId = Guid.NewGuid();
    private readonly Guid _quoteId = Guid.NewGuid();

    public GetCommodityPricesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetCommodityPricesHandler(_db);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SeedAsync(params CommodityPrice[] prices)
    {
        _db.CommodityPrices.AddRange(prices);
        await _db.SaveChangesAsync(Ct);
    }

    private CommodityPrice MakePrice(
        DateTimeOffset priceAt,
        decimal rate,
        CommodityPriceSource source = CommodityPriceSource.Manual,
        Guid? quoteCurrencyId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            CommodityId = _commodityId,
            QuoteCurrencyId = quoteCurrencyId ?? _quoteId,
            PriceAt = priceAt,
            Rate = rate,
            Source = source,
        };

    [Fact]
    public async Task Handle_DefaultSort_OrdersByPriceAtDesc()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now.AddDays(-2), 10m),
            MakePrice(now, 12m),
            MakePrice(now.AddDays(-1), 11m));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery { CommodityId = _commodityId, Page = 1, PageSize = 10 }, Ct);

        Assert.Equal([12m, 11m, 10m], result.Items.Select(p => p.Rate).ToArray());
    }

    [Fact]
    public async Task Handle_NullSortBy_DefaultsToPriceAtDesc()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now.AddDays(-1), 10m),
            MakePrice(now, 11m));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                SortBy = null,
                SortDir = null,
            }, Ct);

        Assert.Equal([11m, 10m], result.Items.Select(p => p.Rate).ToArray());
    }

    [Fact]
    public async Task Handle_SortByPriceAtAsc_OrdersOldestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now, 12m),
            MakePrice(now.AddDays(-2), 10m),
            MakePrice(now.AddDays(-1), 11m));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                SortBy = CommodityPriceSortBy.PriceAt,
                SortDir = SortDirection.Asc,
            }, Ct);

        Assert.Equal([10m, 11m, 12m], result.Items.Select(p => p.Rate).ToArray());
    }

    [Fact]
    public async Task Handle_SortByRateDesc_OrdersByHighestRateFirst()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now, 5m),
            MakePrice(now.AddMinutes(-1), 100m),
            MakePrice(now.AddMinutes(-2), 50m));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                SortBy = CommodityPriceSortBy.Rate,
                SortDir = SortDirection.Desc,
            }, Ct);

        Assert.Equal([100m, 50m, 5m], result.Items.Select(p => p.Rate).ToArray());
    }

    [Fact]
    public async Task Handle_SortByRateAsc_OrdersByLowestRateFirst()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now, 100m),
            MakePrice(now.AddMinutes(-1), 5m),
            MakePrice(now.AddMinutes(-2), 50m));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                SortBy = CommodityPriceSortBy.Rate,
                SortDir = SortDirection.Asc,
            }, Ct);

        Assert.Equal([5m, 50m, 100m], result.Items.Select(p => p.Rate).ToArray());
    }

    [Fact]
    public async Task Handle_SortBySourceAsc_OrdersByEnumValue()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now.AddMinutes(-1), 1m, CommodityPriceSource.Manual),
            MakePrice(now.AddMinutes(-2), 2m, CommodityPriceSource.Transaction),
            MakePrice(now.AddMinutes(-3), 3m, CommodityPriceSource.AlphaVantage));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                SortBy = CommodityPriceSortBy.Source,
                SortDir = SortDirection.Asc,
            }, Ct);

        // Enum order: Transaction(0), Manual(1), BankOfCanada(2), AlphaVantage(3)
        Assert.Equal(
            [CommodityPriceSource.Transaction, CommodityPriceSource.Manual, CommodityPriceSource.AlphaVantage],
            result.Items.Select(p => p.Source).ToArray());
    }

    [Fact]
    public async Task Handle_SortBySourceDesc_OrdersByEnumValueDesc()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now.AddMinutes(-1), 1m, CommodityPriceSource.Manual),
            MakePrice(now.AddMinutes(-2), 2m, CommodityPriceSource.Transaction),
            MakePrice(now.AddMinutes(-3), 3m, CommodityPriceSource.AlphaVantage));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                SortBy = CommodityPriceSortBy.Source,
                SortDir = SortDirection.Desc,
            }, Ct);

        Assert.Equal(
            [CommodityPriceSource.AlphaVantage, CommodityPriceSource.Manual, CommodityPriceSource.Transaction],
            result.Items.Select(p => p.Source).ToArray());
    }

    [Fact]
    public async Task Handle_SortByQuote_OrdersByQuoteCurrencyId()
    {
        var quoteA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var quoteB = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now, 1m, quoteCurrencyId: quoteB),
            MakePrice(now.AddMinutes(-1), 2m, quoteCurrencyId: quoteA),
            MakePrice(now.AddMinutes(-2), 3m, quoteCurrencyId: quoteB));

        var ascResult = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                SortBy = CommodityPriceSortBy.Quote,
                SortDir = SortDirection.Asc,
            }, Ct);

        Assert.Equal(quoteA, ascResult.Items[0].QuoteCurrencyId);
        Assert.Equal(quoteB, ascResult.Items[1].QuoteCurrencyId);
        Assert.Equal(quoteB, ascResult.Items[2].QuoteCurrencyId);

        var descResult = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                SortBy = CommodityPriceSortBy.Quote,
                SortDir = SortDirection.Desc,
            }, Ct);

        Assert.Equal(quoteB, descResult.Items[0].QuoteCurrencyId);
        Assert.Equal(quoteB, descResult.Items[1].QuoteCurrencyId);
        Assert.Equal(quoteA, descResult.Items[2].QuoteCurrencyId);
    }

    [Fact]
    public async Task Handle_SourcesFilter_SingleValue_ReturnsOnlyMatching()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now, 1m, CommodityPriceSource.Manual),
            MakePrice(now.AddMinutes(-1), 2m, CommodityPriceSource.Transaction),
            MakePrice(now.AddMinutes(-2), 3m, CommodityPriceSource.AlphaVantage));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                Sources = [CommodityPriceSource.Manual],
            }, Ct);

        Assert.Single(result.Items);
        Assert.Equal(CommodityPriceSource.Manual, result.Items[0].Source);
    }

    [Fact]
    public async Task Handle_SourcesFilter_MultipleValues_ReturnsAnyMatching()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now, 1m, CommodityPriceSource.Manual),
            MakePrice(now.AddMinutes(-1), 2m, CommodityPriceSource.Transaction),
            MakePrice(now.AddMinutes(-2), 3m, CommodityPriceSource.AlphaVantage),
            MakePrice(now.AddMinutes(-3), 4m, CommodityPriceSource.BankOfCanada));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                Sources = [CommodityPriceSource.Manual, CommodityPriceSource.Transaction],
            }, Ct);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, p =>
            Assert.True(p.Source is CommodityPriceSource.Manual or CommodityPriceSource.Transaction));
    }

    [Fact]
    public async Task Handle_SourcesFilter_Null_ReturnsAll()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now, 1m, CommodityPriceSource.Manual),
            MakePrice(now.AddMinutes(-1), 2m, CommodityPriceSource.Transaction));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                Sources = null,
            }, Ct);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Handle_SourcesFilter_Empty_ReturnsAll()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            MakePrice(now, 1m, CommodityPriceSource.Manual),
            MakePrice(now.AddMinutes(-1), 2m, CommodityPriceSource.Transaction));

        var result = await _sut.Handle(
            new GetCommodityPricesQuery
            {
                CommodityId = _commodityId,
                Page = 1,
                PageSize = 10,
                Sources = [],
            }, Ct);

        Assert.Equal(2, result.Items.Count);
    }
}
