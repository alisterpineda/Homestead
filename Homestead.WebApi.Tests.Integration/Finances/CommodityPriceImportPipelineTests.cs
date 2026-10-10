using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Modules.Finances.Processing.Commands;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Finances;

/// <summary>
/// Real-PostgreSQL coverage for the commodity-price import pipeline. The EF InMemory provider used by the
/// unit tests evaluates the pipeline's GroupBy/Min(Max)/Concat/Distinct queries client-side and does not
/// enforce the unique index, so these behaviours can only be proven against real Npgsql:
///   - <see cref="ImportJob_WatermarkAnchor_TranslatesQueriesAndAnchorsToMinMaxMinusLookback"/> proves the
///     commodity-resolution and min-of-max watermark queries actually translate.
///   - <see cref="ApplyObservations_DuplicateInstantBatch_CollapsesToSingleRowAgainstUniqueIndex"/> proves
///     the last-wins dedup keeps the real unique index from rejecting a duplicate-instant batch.
/// The BankOfCanada provider is faked (see <see cref="FakePriceProvider"/> + HomesteadApiFactory) so the
/// handler runs without any outbound HTTP.
/// </summary>
[Collection("Integration")]
public class CommodityPriceImportPipelineTests : IntegrationTestBase
{
    public CommodityPriceImportPipelineTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ImportJob_WatermarkAnchor_TranslatesQueriesAndAnchorsToMinMaxMinusLookback()
    {
        // Exercises ImportCommodityPricesJobHandler's DB queries on REAL Postgres:
        //   - ResolveCommoditiesAsync: heldFromSplits.Concat(...).Concat(...).Distinct() + Contains(c.Id)
        //   - ResolveImportAnchorAsync: GroupBy(commodity, quote).Select(Max(PriceAt)).Min()  (min-of-max watermark)
        // EF InMemory evaluates both client-side, so the unit suite can't prove they translate; this does.
        var usdId = await CommodityIdAsync("USD");
        var cadId = await CommodityIdAsync("CAD");
        var eurId = await CommodityIdAsync("EUR");

        await EnsureBankOfCanadaSettingsRowAsync();

        // A held account with USD + EUR positions so both currencies survive commodity resolution.
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = new Account
            {
                Id = Guid.NewGuid(),
                Name = "Multi-currency",
                AccountType = AccountType.Asset,
                ReportingCurrencyId = cadId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Accounts.Add(account);

            var tx = new NormalTransaction
            {
                Id = Guid.NewGuid(),
                OccurredAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                Memo = "seed",
                Type = TransactionType.Normal,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.AccountTransactions.Add(tx);

            db.AccountTransactionSplits.Add(new AccountTransactionSplit
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                AccountId = account.Id,
                CommodityId = usdId,
                Quantity = 100m,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.AccountTransactionSplits.Add(new AccountTransactionSplit
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                AccountId = account.Id,
                CommodityId = eurId,
                Quantity = 50m,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(CT);
        }

        // Prior BankOfCanada prices: USD/CAD newer than EUR/CAD, so min(max) == eurLatest.
        var usdLatest = new DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero);
        var eurLatest = new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero);
        await SeedBankOfCanadaPriceAsync(usdId, cadId, usdLatest, 1.36m);
        await SeedBankOfCanadaPriceAsync(eurId, cadId, eurLatest, 1.50m);

        // Invoke the job handler directly (this is read-only query work) and capture the resolved request.
        // Resolve the fake from the SAME scope the handler uses, so the instance we read is the instance the
        // handler's GetKeyedService returns — no static state, no race with background jobs.
        using (var scope = CreateScope())
        {
            var handler = scope.ServiceProvider.GetServices<IJobHandler>()
                .Single(h => h.Type == ProcessingJobType.ImportCommodityPrices);
            var fake = (FakePriceProvider)scope.ServiceProvider
                .GetRequiredKeyedService<IPriceProvider>(CommodityPriceSource.BankOfCanada);

            var job = new ProcessingJob
            {
                Id = Guid.NewGuid(),
                Type = ProcessingJobType.ImportCommodityPrices,
                Status = ProcessingJobStatus.InProgress,
                CreatedAt = DateTimeOffset.UtcNow,
                ScheduleId = Guid.NewGuid(),
            };
            job.SetPayload(new ImportCommodityPricesPayload(CommodityPriceSource.BankOfCanada));

            // Completing without an Npgsql "could not be translated" exception is itself the core proof
            // that the Concat/Distinct/Contains + GroupBy/Min(Max) queries translate to SQL.
            await handler.HandleAsync(job, CT);

            Assert.NotNull(fake.LastRequest);
            Assert.Equal(PriceImportAnchorSource.Watermark, fake.LastRequest!.AnchorSource);
            // min(max(PriceAt)) over the two held pairs is eurLatest; the provider's 5-day revision lookback
            // is then subtracted. This pins that the nested aggregate actually computed in Postgres.
            Assert.Equal(eurLatest - TimeSpan.FromDays(5), fake.LastRequest.FromAt);
        }
    }

    [Fact]
    public async Task ApplyObservations_DuplicateInstantBatch_CollapsesToSingleRowAgainstUniqueIndex()
    {
        // ApplyCommodityPriceObservationsHandler dedups a batch to last-wins BEFORE inserting, precisely so
        // it never emits two rows for one (commodity, quote, instant, source) key. The real unique index
        // enforces that; EF InMemory does not, so the unit test passes whether or not the collapse is correct.
        // This proves the collapse holds against the REAL index — a regression would surface as a 23505.
        var usdId = await CommodityIdAsync("USD");
        var cadId = await CommodityIdAsync("CAD");
        var at = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        using (var scope = CreateScope())
        {
            // Run inside the unit of work, as the import job does, so the transaction commits — the handler
            // has no explicit SaveChanges, so a bare call would persist nothing.
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var handler = scope.ServiceProvider.GetRequiredService<IApplyCommodityPriceObservationsHandler>();
            var command = new ApplyCommodityPriceObservationsCommand
            {
                Source = CommodityPriceSource.BankOfCanada,
                Observations = new[]
                {
                    new PriceObservation(usdId, cadId, at, 1.36m),
                    new PriceObservation(usdId, cadId, at, 1.40m), // duplicate instant, different rate
                },
            };
            await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await handler.Handle(command, CT);
                return true;
            }, CT);
            // No Assert.ThrowsAsync: the assertion is that this does NOT throw a unique-violation.
        }

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.CommodityPrices.AsNoTracking()
                .Where(p => p.CommodityId == usdId && p.QuoteCurrencyId == cadId
                    && p.PriceAt == at && p.Source == CommodityPriceSource.BankOfCanada)
                .ToListAsync(CT);
            var row = Assert.Single(rows);
            Assert.Equal(1.40m, row.Rate); // last-wins
        }
    }

    private async Task<Guid> CommodityIdAsync(string symbol)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Commodities.Where(c => c.Symbol == symbol).Select(c => c.Id).SingleAsync(CT);
    }

    // Enablement is config (HomesteadApiFactory sets PriceSources:BankOfCanada:Enabled); the settings row
    // only carries the watch-list + import bookkeeping, and is ensured here so LastImport* has somewhere
    // to land.
    private async Task EnsureBankOfCanadaSettingsRowAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var exists = await db.CommodityPriceSourceSettings
            .AnyAsync(s => s.Source == CommodityPriceSource.BankOfCanada, CT);
        if (exists) return;

        db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
        });
        await db.SaveChangesAsync(CT);
    }

    private async Task SeedBankOfCanadaPriceAsync(Guid commodityId, Guid quoteCurrencyId, DateTimeOffset priceAt, decimal rate)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CommodityPrices.Add(new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = commodityId,
            QuoteCurrencyId = quoteCurrencyId,
            PriceAt = priceAt,
            Rate = rate,
            Source = CommodityPriceSource.BankOfCanada,
        });
        await db.SaveChangesAsync(CT);
    }
}
