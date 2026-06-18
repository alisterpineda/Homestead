using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccount;
using Homestead.WebApi.Modules.Finances.Features.Settings;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Accounts;

public class GetAccountHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetAccountHandler _sut = null!;
    private Commodity _cad = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(_cad);
        await _db.SaveChangesAsync();
        await SetDisplayCurrencyAsync(_cad.Id);

        _sut = new GetAccountHandler(_db, new ValuationService(_db));
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SetDisplayCurrencyAsync(Guid commodityId)
    {
        var json = JsonSerializer.Serialize(new FinanceSettings { DisplayCurrencyId = commodityId }, FieldChange.JsonOptions);
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.FinancesModule,
            Value = json,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private async Task<Account> AddCashAccountAsync()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Chequing",
            AccountType = AccountKind.Cash.ToAccountType(),
            Kind = AccountKind.Cash,
            ReportingCurrencyId = _cad.Id,
            IsSystem = false,
            IsArchived = false,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    private async Task AddSplitAsync(Guid accountId, decimal quantity, DateTimeOffset occurredAt)
    {
        var txn = new NormalTransaction
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            Memo = "txn",
            Type = TransactionType.Normal,
        };
        _db.AccountTransactions.Add(txn);
        _db.AccountTransactionSplits.Add(new AccountTransactionSplit
        {
            Id = Guid.NewGuid(),
            TransactionId = txn.Id,
            AccountId = accountId,
            CommodityId = _cad.Id,
            Quantity = quantity,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task AsOfBeforeLatestSplit_ExcludesSplitsAfterCutoff()
    {
        var account = await AddCashAccountAsync();
        await AddSplitAsync(account.Id, 100m, new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(account.Id, 50m, new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));

        // Cutoff before the second split — only the first one should count.
        var asOf = new DateTimeOffset(2026, 4, 15, 23, 59, 59, TimeSpan.Zero);
        var resp = await _sut.Handle(new GetAccountQuery { Id = account.Id, AsOf = asOf }, Ct);

        Assert.NotNull(resp);
        Assert.Equal(100m, resp.NativeBalance);
    }

    [Fact]
    public async Task AsOfAfterAllSplits_IncludesAllSplits()
    {
        var account = await AddCashAccountAsync();
        await AddSplitAsync(account.Id, 100m, new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(account.Id, 50m, new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));

        var asOf = new DateTimeOffset(2026, 4, 30, 23, 59, 59, TimeSpan.Zero);
        var resp = await _sut.Handle(new GetAccountQuery { Id = account.Id, AsOf = asOf }, Ct);

        Assert.NotNull(resp);
        Assert.Equal(150m, resp.NativeBalance);
    }

    [Fact]
    public async Task NullAsOf_DefaultsToCurrentDayCutoff_IncludesPastSplits()
    {
        var account = await AddCashAccountAsync();
        // Split well in the past so default "today" cutoff includes it.
        await AddSplitAsync(account.Id, 200m, new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero));

        var resp = await _sut.Handle(new GetAccountQuery { Id = account.Id }, Ct);

        Assert.NotNull(resp);
        Assert.Equal(200m, resp.NativeBalance);
    }

    [Fact]
    public async Task UnknownId_ReturnsNull()
    {
        var resp = await _sut.Handle(new GetAccountQuery { Id = Guid.NewGuid() }, Ct);
        Assert.Null(resp);
    }

    [Fact]
    public async Task SystemAccountId_ReturnsNull()
    {
        var systemAccount = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Equity:Opening Balances",
            AccountType = AccountType.Equity,
            Kind = null,
            ReportingCurrencyId = _cad.Id,
            IsSystem = true,
            IsArchived = false,
        };
        _db.Accounts.Add(systemAccount);
        await _db.SaveChangesAsync(Ct);

        var resp = await _sut.Handle(new GetAccountQuery { Id = systemAccount.Id }, Ct);
        Assert.Null(resp);
    }
}
