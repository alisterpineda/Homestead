using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Processing.Commands;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Processing;

public class ApplyCommodityPriceObservationsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly ApplyCommodityPriceObservationsHandler _sut;

    private readonly Guid _usd = Guid.NewGuid();
    private readonly Guid _cad = Guid.NewGuid();

    public ApplyCommodityPriceObservationsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _sut = new ApplyCommodityPriceObservationsHandler(_db, _audit);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedSettingsAsync()
    {
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_InsertsNewAndUpdatesExisting()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedSettingsAsync();

        var existingAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var newAt = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero);
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _usd,
            QuoteCurrencyId = _cad,
            PriceAt = existingAt,
            Rate = 1.30m,
            Source = CommodityPriceSource.BankOfCanada,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        await _sut.Handle(new ApplyCommodityPriceObservationsCommand
        {
            Source = CommodityPriceSource.BankOfCanada,
            Observations = new[]
            {
                new PriceObservation(_usd, _cad, existingAt, 1.36m),
                new PriceObservation(_usd, _cad, newAt, 1.37m),
            },
        }, ct);

        // The command handler does not save — the UnitOfWorkBehavior commits in production.
        await _db.SaveChangesAsync(ct);

        var prices = _db.CommodityPrices.Where(p => p.Source == CommodityPriceSource.BankOfCanada).ToList();
        Assert.Equal(2, prices.Count);
        Assert.Equal(1.36m, prices.Single(p => p.PriceAt == existingAt).Rate);
        Assert.Equal(1.37m, prices.Single(p => p.PriceAt == newAt).Rate);

        _audit.Received(1).Created("CommodityPrice", Arg.Any<Guid>(), Arg.Any<Guid?>());
        _audit.Received(1).TrackScalar("CommodityPrice", Arg.Any<Guid>(), "Rate",
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_UnchangedRate_DoesNotAuditOrModify()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedSettingsAsync();

        var at = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _usd,
            QuoteCurrencyId = _cad,
            PriceAt = at,
            Rate = 1.30m,
            Source = CommodityPriceSource.BankOfCanada,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        await _sut.Handle(new ApplyCommodityPriceObservationsCommand
        {
            Source = CommodityPriceSource.BankOfCanada,
            Observations = new[] { new PriceObservation(_usd, _cad, at, 1.30m) },
        }, ct);
        await _db.SaveChangesAsync(ct);

        var price = Assert.Single(_db.CommodityPrices.Where(p => p.Source == CommodityPriceSource.BankOfCanada));
        Assert.Equal(1.30m, price.Rate);
        _audit.DidNotReceive().Created(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>());
        _audit.DidNotReceive().TrackScalar(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_DuplicateObservationsInBatch_LastWinsAsSingleRow()
    {
        // A provider batch with two observations for the same (commodity, quote, instant) must not
        // produce two inserts (which would violate the unique index); the last value wins as one row.
        var ct = TestContext.Current.CancellationToken;
        await SeedSettingsAsync();

        var at = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        await _sut.Handle(new ApplyCommodityPriceObservationsCommand
        {
            Source = CommodityPriceSource.BankOfCanada,
            Observations = new[]
            {
                new PriceObservation(_usd, _cad, at, 1.36m),
                new PriceObservation(_usd, _cad, at, 1.40m),
            },
        }, ct);
        await _db.SaveChangesAsync(ct);

        var price = Assert.Single(_db.CommodityPrices.Where(p => p.Source == CommodityPriceSource.BankOfCanada));
        Assert.Equal(1.40m, price.Rate);
        _audit.Received(1).Created("CommodityPrice", Arg.Any<Guid>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_MissingSettingsRow_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        // Note: no SeedSettingsAsync — the settings row is absent.

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.Handle(
            new ApplyCommodityPriceObservationsCommand
            {
                Source = CommodityPriceSource.BankOfCanada,
                Observations = Array.Empty<PriceObservation>(),
            }, ct));
    }

    [Fact]
    public async Task Handle_SuccessResult_StampsSuccessStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedSettingsAsync();

        await _sut.Handle(new ApplyCommodityPriceObservationsCommand
        {
            Source = CommodityPriceSource.BankOfCanada,
            Observations = Array.Empty<PriceObservation>(),
            Warning = null,
        }, ct);
        await _db.SaveChangesAsync(ct);

        var settings = _db.CommodityPriceSourceSettings.Single(s => s.Source == CommodityPriceSource.BankOfCanada);
        Assert.Equal(ImportStatus.Success, settings.LastImportStatus);
        Assert.Null(settings.LastImportMessage);
        Assert.NotNull(settings.LastImportAt);
    }

    [Fact]
    public async Task Handle_Warning_StampsPartialSuccessAndMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedSettingsAsync();

        await _sut.Handle(new ApplyCommodityPriceObservationsCommand
        {
            Source = CommodityPriceSource.BankOfCanada,
            Observations = Array.Empty<PriceObservation>(),
            Warning = "Throttled by upstream.",
        }, ct);
        await _db.SaveChangesAsync(ct);

        var settings = _db.CommodityPriceSourceSettings.Single(s => s.Source == CommodityPriceSource.BankOfCanada);
        Assert.Equal(ImportStatus.PartialSuccess, settings.LastImportStatus);
        Assert.Equal("Throttled by upstream.", settings.LastImportMessage);
        Assert.NotNull(settings.LastImportAt);
    }
}
