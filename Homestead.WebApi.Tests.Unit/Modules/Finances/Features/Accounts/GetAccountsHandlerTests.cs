using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;
using Homestead.WebApi.Modules.Finances.Features.Settings;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Accounts;

public class GetAccountsHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetAccountsHandler _sut = null!;
    private Commodity _cad = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(_cad);
        await _db.SaveChangesAsync();
        await SetDisplayCurrencyAsync(_cad.Id);

        _sut = new GetAccountsHandler(_db, new ValuationService(_db));
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

    private async Task<Account> AddCashAccountAsync(string name = "Chequing")
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
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

    private async Task<Account> AddSystemAccountAsync(string name = "Equity:Opening Balances")
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = AccountType.Equity,
            Kind = null,
            ReportingCurrencyId = _cad.Id,
            IsSystem = true,
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
    public async Task SystemAccounts_AreExcludedFromListAndCount()
    {
        var userAccount = await AddCashAccountAsync();
        await AddSystemAccountAsync();

        var resp = await _sut.Handle(new GetAccountsQuery { Page = 1, PageSize = 10 }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal(userAccount.Id, resp.Items[0].Id);
        Assert.Equal(1, resp.TotalCount);
    }

    [Fact]
    public async Task AsOfBeforeLatestSplit_ExcludesSplitsAfterCutoff()
    {
        var account = await AddCashAccountAsync();
        await AddSplitAsync(account.Id, 100m, new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero));
        await AddSplitAsync(account.Id, 50m, new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));

        var asOf = new DateTimeOffset(2026, 4, 15, 23, 59, 59, TimeSpan.Zero);
        var resp = await _sut.Handle(new GetAccountsQuery { Page = 1, PageSize = 10, AsOf = asOf }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal(100m, resp.Items[0].NativeBalance);
    }

    [Fact]
    public async Task NullAsOf_DefaultsToCurrentDayCutoff_IncludesPastSplits()
    {
        var account = await AddCashAccountAsync();
        await AddSplitAsync(account.Id, 200m, new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero));

        var resp = await _sut.Handle(new GetAccountsQuery { Page = 1, PageSize = 10 }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal(200m, resp.Items[0].NativeBalance);
    }
}
