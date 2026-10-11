using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Processing.Commands;
using System.Net;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Processing;

public class ImportCommodityPricesJobHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IPriceProvider _provider;
    private readonly StubPriceSourceRegistry _sources;
    private readonly IUnitOfWork _unitOfWork;
    private bool _inTransaction;
    private bool _handledInTransaction;
    private readonly IApplyCommodityPriceObservationsHandler _applyObservations;
    private readonly ImportCommodityPricesJobHandler _sut;

    private readonly Commodity _cad;
    private readonly Commodity _usd;

    public ImportCommodityPricesJobHandlerTests()
    {
        _db = TestAppDbContext.Create();

        // Enabled + configured by default: enablement is static config, not a settings row.
        _provider = TestPriceSources.Provider(CommodityPriceSource.BankOfCanada);
        _provider.SupportedPairs.Returns(new[] { new PriceProviderSupportedPair("USD", null, "CAD") });

        _sources = new StubPriceSourceRegistry();
        _sources.Providers[CommodityPriceSource.BankOfCanada] = _provider;

        _applyObservations = Substitute.For<IApplyCommodityPriceObservationsHandler>();

        // Run the work handed to the transaction, so the command handler is reached through it, and record
        // whether the command handler ran while that work was in progress.
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                _inTransaction = true;
                try { return await call.Arg<Func<Task<bool>>>()(); }
                finally { _inTransaction = false; }
            });
        _applyObservations.When(x => x.HandleAsync(Arg.Any<ApplyCommodityPriceObservationsCommand>(), Arg.Any<CancellationToken>()))
            .Do(_ => _handledInTransaction = _inTransaction);

        _sut = new ImportCommodityPricesJobHandler(
            _db, _sources, _unitOfWork, _applyObservations, NullLogger<ImportCommodityPricesJobHandler>.Instance);

        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "Canadian Dollar", Kind = CommodityKind.Currency };
        _usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "US Dollar", Kind = CommodityKind.Currency };
        _db.Commodities.AddRange(_cad, _usd);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

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

    // ---------------------------------------------------------------------------------------------
    // Retry policy and the final-failure hook
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void OptsIntoRetries()
    {
        IJobHandler handler = _sut;
        Assert.Equal(3, handler.MaxAttempts);
    }

    [Fact]
    public void ImportBlockers_AreNotRetried()
    {
        IJobHandler handler = _sut;
        Assert.False(handler.ShouldRetry(new PriceImportException("source_disabled", "Disabled.")));
        Assert.True(handler.ShouldRetry(new HttpRequestException("Unavailable", null, HttpStatusCode.ServiceUnavailable)));
    }

    /// <summary>
    /// A failed attempt no longer stamps the settings row itself: with retries that would read Failed while
    /// the next attempt was still queued. The hook does it, once, after the last attempt.
    /// </summary>
    [Fact]
    public async Task FailedAttempt_DoesNotStampTheSettingsRow()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedSettings(CommodityPriceSource.BankOfCanada);
        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<PriceImportResult>>(_ => throw new HttpRequestException("Valet is down"));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await Assert.ThrowsAsync<HttpRequestException>(() => _sut.HandleAsync(job, ct));

        var settings = await _db.CommodityPriceSourceSettings.SingleAsync(ct);
        Assert.Null(settings.LastImportStatus);
    }

    [Fact]
    public async Task FinalFailureHook_StampsTheSettingsRowFailed_WithATruncatedMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedSettings(CommodityPriceSource.BankOfCanada);
        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));

        IJobHandler handler = _sut;
        await handler.OnFinallyFailedAsync(job, new InvalidOperationException(new string('x', 5000)), ct);

        var settings = await _db.CommodityPriceSourceSettings.SingleAsync(ct);
        Assert.Equal(ImportStatus.Failed, settings.LastImportStatus);
        Assert.Equal(4096, settings.LastImportMessage!.Length);
    }

    [Fact]
    public async Task FinalFailureHook_IgnoresSystemManagedSources()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedSettings(CommodityPriceSource.Manual);
        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.Manual));

        IJobHandler handler = _sut;
        await handler.OnFinallyFailedAsync(job, new InvalidOperationException("boom"), ct);

        var settings = await _db.CommodityPriceSourceSettings.SingleAsync(ct);
        Assert.Null(settings.LastImportStatus);
    }

    private void SeedSettings(CommodityPriceSource source)
    {
        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = source,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task NoSettingsRow_ImportsWithEmptyAllowList()
    {
        // The settings row only carries the watch-list now; its absence is not a failure.
        await SeedHeldUsdAccountAsync();

        IReadOnlyCollection<Commodity>? captured = null;
        _provider.ImportAsync(Arg.Do<PriceImportRequest>(r => captured = r.Commodities), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Contains(captured!, c => c.Id == _usd.Id);
    }

    [Fact]
    public async Task DisabledSource_Throws_SourceDisabled()
    {
        _provider.IsEnabled.Returns(false);
        _provider.IsConfigured.Returns(false);

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        var ex = await Assert.ThrowsAsync<PriceImportException>(
            () => _sut.HandleAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal("source_disabled", ex.ErrorCode);
    }

    [Fact]
    public async Task NotConfiguredSource_Throws_SourceNotConfigured()
    {
        _provider.IsEnabled.Returns(true);
        _provider.IsConfigured.Returns(false);
        _provider.ConfigurationError.Returns("ApiKey is required.");

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        var ex = await Assert.ThrowsAsync<PriceImportException>(
            () => _sut.HandleAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal("source_not_configured", ex.ErrorCode);
        Assert.Contains("ApiKey is required.", ex.Message);
    }

    [Fact]
    public async Task ProviderUnregistered_Throws_ProviderNotRegistered()
    {
        _sources.Providers.Clear();

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        var ex = await Assert.ThrowsAsync<PriceImportException>(
            () => _sut.HandleAsync(job, TestContext.Current.CancellationToken));
        Assert.Equal("provider_not_registered", ex.ErrorCode);
    }

    [Fact]
    public async Task Import_AppliesObservationsCommand_WithProviderObservations()
    {
        // The DB upsert + audit moved into ApplyCommodityPriceObservationsHandler (tested separately).
        // The job handler's responsibility is to hand the provider's observations off via the command.
        var ct = TestContext.Current.CancellationToken;
        await SeedHeldUsdAccountAsync();

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

        await _applyObservations.Received(1).HandleAsync(
            Arg.Is<ApplyCommodityPriceObservationsCommand>(c =>
                c.Source == CommodityPriceSource.BankOfCanada
                && c.Observations.Count == 2
                && c.Warning == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunNow_SetsActorToCreatingUser_ThenAppliesInsideTheTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedHeldUsdAccountAsync();

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

        // Manual runs are attributed to the initiating user. The provider call holds no transaction; the
        // actor is set before the transaction opens and the command runs inside it.
        Received.InOrder(() =>
        {
            _provider.ImportAsync(Arg.Any<PriceImportRequest>(), ct);
            _unitOfWork.SetActor(creator);
            _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<bool>>>(), ct);
        });
        Assert.True(_handledInTransaction);
    }

    [Fact]
    public async Task ScheduledRun_SetsNoActor_ThenAppliesInsideTheTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedHeldUsdAccountAsync();

        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(new[]
            {
                new PriceObservation(_usd.Id, _cad.Id, DateTimeOffset.UtcNow, 1.30m),
            }));

        var job = MakeJob(
            new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada),
            scheduleId: Guid.NewGuid(), userId: Guid.NewGuid());

        await _sut.HandleAsync(job, ct);

        // Scheduled imports stay system-attributed (null actor), even though the job has a creator.
        Received.InOrder(() =>
        {
            _unitOfWork.SetActor(null);
            _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<bool>>>(), ct);
        });
        Assert.True(_handledInTransaction);
        _unitOfWork.DidNotReceive().SetActor(Arg.Is<Guid?>(id => id != null));
    }

    [Fact]
    public async Task SuccessResult_AppliesCommandWithNullWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedHeldUsdAccountAsync();

        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Success(Array.Empty<PriceObservation>()));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, ct);

        // The command runs even with zero observations so the status stamp always runs.
        await _applyObservations.Received(1).HandleAsync(
            Arg.Is<ApplyCommodityPriceObservationsCommand>(c => c.Warning == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayloadCommodityIds_FilterApplied()
    {
        await SeedHeldUsdAccountAsync();

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
    public async Task PartialSuccess_AppliesCommandWithWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedHeldUsdAccountAsync();

        var priceAt = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
        _provider.ImportAsync(Arg.Any<PriceImportRequest>(), Arg.Any<CancellationToken>())
            .Returns(PriceImportResult.Partial(
                new[] { new PriceObservation(_usd.Id, _cad.Id, priceAt, 1.40m) },
                "Throttled by upstream after 5 calls."));

        var job = MakeJob(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));
        await _sut.HandleAsync(job, ct);

        await _applyObservations.Received(1).HandleAsync(
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
