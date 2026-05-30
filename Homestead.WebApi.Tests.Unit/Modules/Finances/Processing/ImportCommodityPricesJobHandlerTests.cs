using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Processing.Commands;
using Homestead.WebApi.Persistence;
using MediatR;
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
    private readonly ISender _sender;
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
        _provider.CoveredSecurityExchanges.Returns(Array.Empty<string>());
        _provider.CoveredQuoteCurrencies.Returns(Array.Empty<string>());

        var sc = new ServiceCollection();
        sc.AddKeyedSingleton(CommodityPriceSource.BankOfCanada, _provider);
        _services = sc.BuildServiceProvider();
        _emptyServices = new ServiceCollection().BuildServiceProvider();

        _sender = Substitute.For<ISender>();

        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var scopedSc = new ServiceCollection();
        scopedSc.AddSingleton<AppDbContext>(_db);
        scope.ServiceProvider.Returns(scopedSc.BuildServiceProvider());
        _scopeFactory.CreateScope().Returns(scope);

        _sut = new ImportCommodityPricesJobHandler(
            _db, _services, _sender, _scopeFactory, NullLogger<ImportCommodityPricesJobHandler>.Instance);

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
            _db, _emptyServices, _sender, _scopeFactory, NullLogger<ImportCommodityPricesJobHandler>.Instance);

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        var ex = await Assert.ThrowsAsync<PriceImportException>(
            () => sut.HandleAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal("provider_not_registered", ex.ErrorCode);
    }

    [Fact]
    public async Task Import_SendsApplyObservationsCommand_WithProviderObservations()
    {
        // The DB upsert + audit moved into ApplyCommodityPriceObservationsHandler (tested separately).
        // The job handler's responsibility is to hand the provider's observations off via the command.
        var ct = TestContext.Current.CancellationToken;
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(ct);

        var at1 = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var at2 = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero);
        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(new[]
            {
                new PriceObservation(_usd.Id, _cad.Id, at1, 1.36m),
                new PriceObservation(_usd.Id, _cad.Id, at2, 1.37m),
            }));

        var job = MakeJob(
            new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada),
            userId: Guid.NewGuid());
        await _sut.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<ApplyCommodityPriceObservationsCommand>(c =>
                c.Source == CommodityPriceSource.BankOfCanada
                && c.Observations.Count == 2
                && c.Warning == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunNow_SendsCommandAttributedToCreatingUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(ct);

        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(new[]
            {
                new PriceObservation(_usd.Id, _cad.Id, DateTimeOffset.UtcNow, 1.30m),
            }));

        var creator = Guid.NewGuid();
        var job = MakeJob(
            new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada),
            scheduleId: null, userId: creator);

        await _sut.HandleAsync(job, ct);

        // Manual runs are attributed to the initiating user via the command's ActorUserId.
        await _sender.Received(1).Send(
            Arg.Is<ApplyCommodityPriceObservationsCommand>(c => c.ActorUserId == creator),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScheduledRun_SendsCommandWithNoActor()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(ct);

        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(new[]
            {
                new PriceObservation(_usd.Id, _cad.Id, DateTimeOffset.UtcNow, 1.30m),
            }));

        var job = MakeJob(
            new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada),
            scheduleId: Guid.NewGuid(), userId: Guid.NewGuid());

        await _sut.HandleAsync(job, ct);

        // Scheduled imports stay system-attributed (null actor).
        await _sender.Received(1).Send(
            Arg.Is<ApplyCommodityPriceObservationsCommand>(c => c.ActorUserId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessResult_SendsCommandWithNullWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(ct);

        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, ct);

        // The command is sent even with zero observations so the status stamp always runs.
        await _sender.Received(1).Send(
            Arg.Is<ApplyCommodityPriceObservationsCommand>(c => c.Warning == null),
            Arg.Any<CancellationToken>());
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
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(
            new ImportCommodityPricesPayload(
                CommodityPriceSource.BankOfCanada,
                CommodityIds: new[] { _cad.Id }));

        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.All(captured!, c => Assert.Equal(_cad.Id, c.Id));
    }

    [Fact]
    public async Task CoveredSecurityExchanges_IncludesHeldSecurityNotInSupportedPairs()
    {
        var nasdaq = new Commodity
        {
            Id = Guid.NewGuid(),
            Symbol = "AAPL",
            Exchange = "NASDAQ",
            Name = "Apple",
            Kind = CommodityKind.Security,
        };
        _db.Commodities.Add(nasdaq);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Brokerage",
            AccountType = AccountType.Asset,
            ReportingCurrencyId = _cad.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Accounts.Add(account);

        var tx = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-2),
            Memo = "buy",
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
            CommodityId = nasdaq.Id,
            Quantity = 10m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _provider.CoveredSecurityExchanges.Returns(new[] { "NASDAQ" });

        IReadOnlyCollection<Commodity>? captured = null;
        _provider.ImportAsync(Arg.Do<PriceImportRequest>(r => captured = r.Commodities), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Contains(captured!, c => c.Id == nasdaq.Id);
    }

    [Fact]
    public async Task CoveredQuoteCurrencies_IncludesHeldQuoteCurrencyForUnboundedProvider()
    {
        var nyseSec = new Commodity
        {
            Id = Guid.NewGuid(),
            Symbol = "GME",
            Exchange = "NYSE",
            Name = "GameStop",
            Kind = CommodityKind.Security,
        };
        _db.Commodities.Add(nyseSec);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Brokerage",
            AccountType = AccountType.Asset,
            ReportingCurrencyId = _usd.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Accounts.Add(account);

        var tx = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-2),
            Memo = "buy",
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
            CommodityId = nyseSec.Id,
            Quantity = 10m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.AccountTransactionSplits.Add(new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            AccountId = account.Id,
            CommodityId = _usd.Id,
            Quantity = -1500m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _provider.SupportedPairs.Returns(Array.Empty<PriceProviderSupportedPair>());
        _provider.CoveredSecurityExchanges.Returns(new[] { "NYSE" });
        _provider.CoveredQuoteCurrencies.Returns(new[] { "USD" });

        IReadOnlyCollection<Commodity>? captured = null;
        _provider.ImportAsync(Arg.Do<PriceImportRequest>(r => captured = r.Commodities), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Contains(captured!, c => c.Id == nyseSec.Id);
        Assert.Contains(captured!, c => c.Id == _usd.Id);
    }

    [Fact]
    public async Task PartialSuccess_SendsCommandWithWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(ct);

        var priceAt = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Partial(
                new[] { new PriceObservation(_usd.Id, _cad.Id, priceAt, 1.40m) },
                "Throttled by upstream after 5 calls."));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, ct);

        await _sender.Received(1).Send(
            Arg.Is<ApplyCommodityPriceObservationsCommand>(c =>
                c.Warning == "Throttled by upstream after 5 calls."
                && c.Observations.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunImport_NoPriorPricesForSource_AnchorsToEarliestActivity()
    {
        var occurredAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        await SeedHeldUsdAccountAsync(occurredAt);
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        PriceImportRequest? captured = null;
        _provider.ImportAsync(Arg.Do<PriceImportRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(PriceImportAnchorSource.EarliestActivity, captured!.AnchorSource);
        Assert.Equal(occurredAt, captured.FromAt);
    }

    [Fact]
    public async Task RunImport_PriorPricesExist_AnchorsToMinMaxPriceAtMinusLookback()
    {
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });

        var eur = new Commodity { Id = Guid.NewGuid(), Symbol = "EUR", Name = "Euro", Kind = CommodityKind.Currency };
        _db.Commodities.Add(eur);
        var eurAccount = new Account
        {
            Id = Guid.NewGuid(),
            Name = "EUR account",
            AccountType = AccountType.Asset,
            ReportingCurrencyId = _cad.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Accounts.Add(eurAccount);
        var eurTx = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-20),
            Memo = "buy EUR",
            Type = TransactionType.Normal,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.AccountTransactions.Add(eurTx);
        _db.AccountTransactionSplits.Add(new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            TransactionId = eurTx.Id,
            AccountId = eurAccount.Id,
            CommodityId = eur.Id,
            Quantity = 100m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        _provider.SupportedPairs.Returns(new[]
        {
            new PriceProviderSupportedPair("USD", null, "CAD"),
            new PriceProviderSupportedPair("EUR", null, "CAD"),
        });

        var usdLatest = new DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero);
        var eurLatest = new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero);
        _db.CommodityPrices.AddRange(
            new CommodityPrice
            {
                Id = Guid.NewGuid(),
                CommodityId = _usd.Id, QuoteCurrencyId = _cad.Id,
                PriceAt = usdLatest, Rate = 1.36m,
                Source = CommodityPriceSource.BankOfCanada,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            },
            new CommodityPrice
            {
                Id = Guid.NewGuid(),
                CommodityId = eur.Id, QuoteCurrencyId = _cad.Id,
                PriceAt = eurLatest, Rate = 1.50m,
                Source = CommodityPriceSource.BankOfCanada,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        PriceImportRequest? captured = null;
        _provider.ImportAsync(Arg.Do<PriceImportRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(PriceImportAnchorSource.Watermark, captured!.AnchorSource);
        Assert.Equal(eurLatest, captured.FromAt);
    }

    [Fact]
    public async Task RunImport_AppliesProviderRevisionLookback()
    {
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });

        var watermark = new DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero);
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _usd.Id, QuoteCurrencyId = _cad.Id,
            PriceAt = watermark, Rate = 1.36m,
            Source = CommodityPriceSource.BankOfCanada,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _provider.RevisionLookback.Returns(TimeSpan.FromDays(7));

        PriceImportRequest? captured = null;
        _provider.ImportAsync(Arg.Do<PriceImportRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(watermark - TimeSpan.FromDays(7), captured!.FromAt);
    }

    [Fact]
    public async Task RunImport_FrozenPairForArchivedCommodity_DoesNotDragAnchor()
    {
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });

        var eur = new Commodity { Id = Guid.NewGuid(), Symbol = "EUR", Name = "Euro", Kind = CommodityKind.Currency };
        _db.Commodities.Add(eur);
        var archivedAccount = new Account
        {
            Id = Guid.NewGuid(),
            Name = "EUR archived",
            AccountType = AccountType.Asset,
            ReportingCurrencyId = _cad.Id,
            IsArchived = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Accounts.Add(archivedAccount);
        var eurTx = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow.AddDays(-200),
            Memo = "old buy",
            Type = TransactionType.Normal,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.AccountTransactions.Add(eurTx);
        _db.AccountTransactionSplits.Add(new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            TransactionId = eurTx.Id,
            AccountId = archivedAccount.Id,
            CommodityId = eur.Id,
            Quantity = 100m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var usdLatest = new DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero);
        var eurFrozen = new DateTimeOffset(2025, 8, 1, 0, 0, 0, TimeSpan.Zero);
        _db.CommodityPrices.AddRange(
            new CommodityPrice
            {
                Id = Guid.NewGuid(),
                CommodityId = _usd.Id, QuoteCurrencyId = _cad.Id,
                PriceAt = usdLatest, Rate = 1.36m,
                Source = CommodityPriceSource.BankOfCanada,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            },
            new CommodityPrice
            {
                Id = Guid.NewGuid(),
                CommodityId = eur.Id, QuoteCurrencyId = _cad.Id,
                PriceAt = eurFrozen, Rate = 1.45m,
                Source = CommodityPriceSource.BankOfCanada,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        PriceImportRequest? captured = null;
        _provider.ImportAsync(Arg.Do<PriceImportRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(PriceImportAnchorSource.Watermark, captured!.AnchorSource);
        Assert.Equal(usdLatest, captured.FromAt);
    }

    [Fact]
    public async Task RunImport_ExplicitPayloadFromAt_BypassesWatermark()
    {
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _usd.Id, QuoteCurrencyId = _cad.Id,
            PriceAt = new DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero),
            Rate = 1.36m,
            Source = CommodityPriceSource.BankOfCanada,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var explicitFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        PriceImportRequest? captured = null;
        _provider.ImportAsync(Arg.Do<PriceImportRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(
            CommodityPriceSource.BankOfCanada,
            FromAt: explicitFrom));
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(PriceImportAnchorSource.ExplicitOverride, captured!.AnchorSource);
        Assert.Equal(explicitFrom, captured.FromAt);
    }

    [Fact]
    public async Task RunImport_ScheduledRun_DoesNotDropMidnightUtcObservations()
    {
        await SeedHeldUsdAccountAsync();
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = true,
        });

        var yesterdayMidnight = new DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero);
        _db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = _usd.Id, QuoteCurrencyId = _cad.Id,
            PriceAt = yesterdayMidnight, Rate = 1.36m,
            Source = CommodityPriceSource.BankOfCanada,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        PriceImportRequest? captured = null;
        _provider.ImportAsync(Arg.Do<PriceImportRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.True(captured!.FromAt <= yesterdayMidnight,
            $"FromAt {captured.FromAt:o} must be <= yesterday-midnight {yesterdayMidnight:o}");
    }

    private async Task SeedHeldUsdAccountAsync(DateTimeOffset? occurredAt = null)
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
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow.AddDays(-10),
            Memo = "seed",
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
