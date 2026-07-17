using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Payees.GetPayees;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Payees;

public class GetPayeesHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetPayeesHandler _sut = null!;

    public ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetPayeesHandler(_db);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Payee> AddPayeeAsync(
        string name, bool isArchived = false, int transactions = 0,
        DateTimeOffset? updatedAt = null, Account? defaultCategory = null)
    {
        var payee = new Payee
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsArchived = isArchived,
            DefaultCategoryId = defaultCategory?.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow,
        };
        _db.Payees.Add(payee);
        for (var i = 0; i < transactions; i++)
        {
            _db.AccountTransactions.Add(new NormalTransaction
            {
                Id = Guid.NewGuid(),
                Memo = $"{name} tx {i}",
                Type = TransactionType.Normal,
                NormalShape = NormalShape.Expense,
                PayeeId = payee.Id,
            });
        }
        await _db.SaveChangesAsync(Ct);
        return payee;
    }

    private async Task<Account> AddCategoryAsync(AccountType type, string name)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = type,
            ReportingCurrencyId = Guid.NewGuid(),
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(Ct);
        return account;
    }

    [Fact]
    public async Task Handle_OrdersByName()
    {
        await AddPayeeAsync("Safeway");
        await AddPayeeAsync("Costco");
        await AddPayeeAsync("Metro");

        var resp = await _sut.Handle(new GetPayeesQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(["Costco", "Metro", "Safeway"], resp.Items.Select(p => p.Name));
    }

    [Fact]
    public async Task Handle_FiltersBySearch_CaseInsensitive()
    {
        await AddPayeeAsync("Costco");
        await AddPayeeAsync("Safeway");

        var resp = await _sut.Handle(new GetPayeesQuery { Page = 1, PageSize = 50, Search = "COST" }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Costco", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_ExcludesArchived_ByDefault()
    {
        await AddPayeeAsync("Costco");
        await AddPayeeAsync("OldStore", isArchived: true);

        var resp = await _sut.Handle(new GetPayeesQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Costco", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_IncludesArchived_WhenRequested()
    {
        await AddPayeeAsync("Costco");
        await AddPayeeAsync("OldStore", isArchived: true);

        var resp = await _sut.Handle(
            new GetPayeesQuery { Page = 1, PageSize = 50, IncludeArchived = true }, Ct);

        Assert.Equal(2, resp.Items.Count);
    }

    [Fact]
    public async Task Handle_PopulatesTransactionCount()
    {
        await AddPayeeAsync("Costco", transactions: 3);
        await AddPayeeAsync("Safeway", transactions: 0);

        var resp = await _sut.Handle(new GetPayeesQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(3, resp.Items.Single(p => p.Name == "Costco").TransactionCount);
        Assert.Equal(0, resp.Items.Single(p => p.Name == "Safeway").TransactionCount);
    }

    [Fact]
    public async Task Handle_PopulatesDefaultCategoryNameAndType()
    {
        var category = await AddCategoryAsync(AccountType.Expense, "Groceries");
        await AddPayeeAsync("Costco", defaultCategory: category);

        var resp = await _sut.Handle(new GetPayeesQuery { Page = 1, PageSize = 50 }, Ct);

        var costco = resp.Items.Single(p => p.Name == "Costco");
        Assert.Equal(category.Id, costco.DefaultCategoryId);
        Assert.Equal("Groceries", costco.DefaultCategoryName);
        Assert.Equal(AccountType.Expense, costco.DefaultCategoryType);
    }

    [Fact]
    public async Task Handle_SortByTransactions_Descending()
    {
        await AddPayeeAsync("Costco", transactions: 1);
        await AddPayeeAsync("Safeway", transactions: 3);
        await AddPayeeAsync("Metro", transactions: 2);

        var resp = await _sut.Handle(
            new GetPayeesQuery { Page = 1, PageSize = 50, SortBy = "transactions", SortDir = "desc" }, Ct);

        Assert.Equal(["Safeway", "Metro", "Costco"], resp.Items.Select(p => p.Name));
    }

    [Fact]
    public async Task Handle_SortByUpdated_Ascending()
    {
        var now = DateTimeOffset.UtcNow;
        await AddPayeeAsync("Newest", updatedAt: now);
        await AddPayeeAsync("Oldest", updatedAt: now.AddDays(-2));
        await AddPayeeAsync("Middle", updatedAt: now.AddDays(-1));

        var resp = await _sut.Handle(
            new GetPayeesQuery { Page = 1, PageSize = 50, SortBy = "updated", SortDir = "asc" }, Ct);

        Assert.Equal(["Oldest", "Middle", "Newest"], resp.Items.Select(p => p.Name));
    }

    [Fact]
    public async Task Handle_Paginates_TotalCountReflectsWholeSet()
    {
        for (var i = 0; i < 5; i++)
            await AddPayeeAsync($"Payee {i:D2}");

        var resp = await _sut.Handle(new GetPayeesQuery { Page = 1, PageSize = 2 }, Ct);

        Assert.Equal(2, resp.Items.Count);
        Assert.Equal(5, resp.TotalCount);
    }
}
