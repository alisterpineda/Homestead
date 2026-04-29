using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Processing;

public class ImportCommodityPricesJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IPriceProvider _provider;
    private readonly ServiceProvider _services;
    private readonly ServiceProvider _emptyServices;
    private readonly IAuditLogger _auditLogger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImportCommodityPricesJobHandler _sut;

    private readonly Commodity _cad;
    private readonly Commodity _usd;

    public ImportCommodityPricesJobHandlerTests()
    {
        _db = TestAppDbContext.Create();

        _provider = Substitute.For<IPriceProvider>();
        _provider.Source.Returns(CommodityPriceSource.BankOfCanada);
        _provider.SupportedPairs.Returns(new[] { new PriceProviderSupportedPair("USD", null, "CAD") });

        var sc = new ServiceCollection();
        sc.AddKeyedSingleton(CommodityPriceSource.BankOfCanada, _provider);
        _services = sc.BuildServiceProvider();
        _emptyServices = new ServiceCollection().BuildServiceProvider();

        _auditLogger = Substitute.For<IAuditLogger>();
        _auditLogger.ForUser(Arg.Any<Guid?>()).Returns(_auditLogger);

        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var scopedSc = new ServiceCollection();
        scopedSc.AddSingleton<AppDbContext>(_db);
        scope.ServiceProvider.Returns(scopedSc.BuildServiceProvider());
        _scopeFactory.CreateScope().Returns(scope);

        _sut = new ImportCommodityPricesJobHandler(
            _db, _services, _auditLogger, _scopeFactory, NullLogger<ImportCommodityPricesJobHandler>.Instance);

        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "Canadian Dollar", Kind = CommodityKind.Currency };
        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "US Dollar", Kind = CommodityKind.Currency };
        _db.Commodities.AddRange(_cad, _usd);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _services.Dispose();
        _emptyServices.Dispose();
    }

    private ProcessingJob MakeJob(ImportCommodityPricesPayload payload, Guid? scheduleId = null, Guid? userId = null)
    {
        var job = new ProcessingJob
        {
            Id = Guid.NewGuid(),
            Type = ProcessingJobType.ImportCommodityPrices,
            Status = ProcessingJobStatus.InProgress,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId,
            ScheduleId = scheduleId,
        };
        job.SetPayload(payload);
        return job;
    }

    [Fact]
    public async Task SystemSource_Throws_SourceSystemManaged()
    {
        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.Manual));
        var ex = await Assert.ThrowsAsync<PriceImportException>(
            () => _sut.HandleAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal("source_system_managed", ex.ErrorCode);
    }

    [Fact]
    public async Task NoSettingsRow_Throws_SourceDisabled()
    {
        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        var ex = await Assert.ThrowsAsync<PriceImportException>(
            () => _sut.HandleAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal("source_disabled", ex.ErrorCode);
    }

    [Fact]
    public async Task DisabledSource_Throws_SourceDisabled()
    {
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = false,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        var ex = await Assert.ThrowsAsync<PriceImportException>(
            () => _sut.HandleAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal("source_disabled", ex.ErrorCode);
    }

    [Fact]
    public async Task ProviderUnregistered_Throws_ProviderNotRegistered()
    {
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = new ImportCommodityPricesJobHandler(
            _db, _emptyServices, _auditLogger, _scopeFactory, NullLogger<ImportCommodityPricesJobHandler>.Instance);

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        var ex = await Assert.ThrowsAsync<PriceImportException>(
            () => sut.HandleAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal("provider_not_registered", ex.ErrorCode);
    }

    [Fact]
    public async Task UpsertObservations_InsertsNewAndUpdatesExisting()
    {
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });

        var existingAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var newAt = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero);
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _usd.Id,
            QuoteCurrencyId = _cad.Id,
            PriceAt = existingAt,
            Rate = 1.30m,
            Source = CommodityPriceSource.BankOfCanada,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new PriceObservation(_usd.Id, _cad.Id, existingAt, 1.36m),
                new PriceObservation(_usd.Id, _cad.Id, newAt, 1.37m),
            });

        var job = MakeJob(
            new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada),
            userId: Guid.NewGuid());
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        var prices = _db.CommodityPrices.Where(p => p.Source == CommodityPriceSource.BankOfCanada).ToList();
        Assert.Equal(2, prices.Count);
        Assert.Equal(1.36m, prices.Single(p => p.PriceAt == existingAt).Rate);
        Assert.Equal(1.37m, prices.Single(p => p.PriceAt == newAt).Rate);

        _auditLogger.Received().LogCreated("CommodityPrice", Arg.Any<Guid>(), Arg.Any<Guid?>());
        _auditLogger.Received().LogUpdated("CommodityPrice", Arg.Any<Guid>(),
            Arg.Any<List<Homestead.WebApi.Domain.Auditing.FieldChange>>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task RunNow_AttributesAuditToCreatingUser()
    {
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new PriceObservation(_usd.Id, _cad.Id, DateTimeOffset.UtcNow, 1.30m),
            });

        var creator = Guid.NewGuid();
        var job = MakeJob(
            new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada),
            scheduleId: null, userId: creator);

        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        _auditLogger.Received().ForUser(creator);
    }

    [Fact]
    public async Task ScheduledRun_AttributesAuditAnonymous()
    {
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new PriceObservation(_usd.Id, _cad.Id, DateTimeOffset.UtcNow, 1.30m),
            });

        var job = MakeJob(
            new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada),
            scheduleId: Guid.NewGuid(), userId: Guid.NewGuid());

        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        _auditLogger.Received().ForUser((Guid?)null);
    }

    [Fact]
    public async Task SuccessPath_UpdatesLastImportStatus()
    {
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PriceObservation>());

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        var settings = _db.CommodityPriceSourceSettings.Single(s => s.Source == CommodityPriceSource.BankOfCanada);
        Assert.Equal("Success", settings.LastImportStatus);
        Assert.Null(settings.LastImportError);
        Assert.NotNull(settings.LastImportAt);
    }

    [Fact]
    public async Task PayloadCommodityIds_FilterApplied()
    {
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyCollection<Commodity>? captured = null;
        _provider.ImportAsync(Arg.Do<PriceImportRequest>(r => captured = r.Commodities), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PriceObservation>());

        var job = MakeJob(
            new ImportCommodityPricesPayload(
                CommodityPriceSource.BankOfCanada,
                CommodityIds: new[] { _cad.Id }));

        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.All(captured!, c => Assert.Equal(_cad.Id, c.Id));
    }

    private async Task SeedHeldUsdAccountAsync()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "USD chequing",
            AccountType = AccountType.Asset,
            ReportingCurrencyId = _cad.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tx = new Homestead.WebApi.Domain.Finances.NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-10),
            Description = "seed",
            Type = TransactionType.Normal,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.AccountTransactions.Add(tx);

        _db.AccountTransactionSplits.Add(new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            AccountId = account.Id,
            CommodityId = _usd.Id,
            Quantity = 100m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
