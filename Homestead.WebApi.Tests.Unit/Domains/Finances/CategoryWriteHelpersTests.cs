using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Categories;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class CategoryWriteHelpersTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private Guid _currencyId;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        var cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(cad);
        _currencyId = cad.Id;
        await _db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Account> Add(AccountType type, string name, Guid? parentId = null, bool isSystem = false)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(), Name = name, AccountType = type, Kind = null,
            ParentAccountId = parentId, ReportingCurrencyId = _currencyId,
            IsSystem = isSystem, IsArchived = false,
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- SiblingNameTakenAsync ----

    [Fact]
    public async Task SiblingNameTaken_SameParentTypeName_CaseInsensitive_True()
    {
        var parent = await Add(AccountType.Expense, "Food");
        await Add(AccountType.Expense, "Groceries", parent.Id);

        Assert.True(await CategoryWriteHelpers.SiblingNameTakenAsync(
            _db, parent.Id, AccountType.Expense, "groceries", null, Ct));
    }

    [Fact]
    public async Task SiblingNameTaken_DifferentType_False()
    {
        await Add(AccountType.Expense, "Salary");
        // Same root/name but the Income type is a different sibling scope.
        Assert.False(await CategoryWriteHelpers.SiblingNameTakenAsync(
            _db, null, AccountType.Income, "Salary", null, Ct));
    }

    [Fact]
    public async Task SiblingNameTaken_RootSiblings_MatchOnNullParent_True()
    {
        await Add(AccountType.Expense, "Rent");
        Assert.True(await CategoryWriteHelpers.SiblingNameTakenAsync(
            _db, null, AccountType.Expense, "Rent", null, Ct));
    }

    [Fact]
    public async Task SiblingNameTaken_ExcludesSelf_False()
    {
        var self = await Add(AccountType.Expense, "Rent");
        // Renaming to the same name is not a duplicate against itself.
        Assert.False(await CategoryWriteHelpers.SiblingNameTakenAsync(
            _db, null, AccountType.Expense, "Rent", self.Id, Ct));
    }

    // ---- ValidateParentAsync ----

    [Fact]
    public async Task ValidateParent_SameTypeCategory_Ok()
    {
        var parent = await Add(AccountType.Expense, "Food");
        await CategoryWriteHelpers.ValidateParentAsync(_db, parent.Id, AccountType.Expense, Ct);
    }

    [Fact]
    public async Task ValidateParent_DifferentType_Throws()
    {
        var parent = await Add(AccountType.Income, "Salary");
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CategoryWriteHelpers.ValidateParentAsync(_db, parent.Id, AccountType.Expense, Ct));
    }

    [Fact]
    public async Task ValidateParent_SystemBucket_Throws()
    {
        var bucket = await Add(AccountType.Expense, "Expense", isSystem: true);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CategoryWriteHelpers.ValidateParentAsync(_db, bucket.Id, AccountType.Expense, Ct));
    }

    [Fact]
    public async Task ValidateParent_NonCategoryAccount_Throws()
    {
        var asset = await Add(AccountType.Asset, "Chequing");
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CategoryWriteHelpers.ValidateParentAsync(_db, asset.Id, AccountType.Expense, Ct));
    }

    [Fact]
    public async Task ValidateParent_Missing_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CategoryWriteHelpers.ValidateParentAsync(_db, Guid.NewGuid(), AccountType.Expense, Ct));
    }

    // ---- GetDescendantIdsAsync (cycle prevention) ----

    [Fact]
    public async Task GetDescendantIds_ReturnsWholeSubtree_ExcludingSelf()
    {
        var food = await Add(AccountType.Expense, "Food");
        var groceries = await Add(AccountType.Expense, "Groceries", food.Id);
        var produce = await Add(AccountType.Expense, "Produce", groceries.Id);
        var rent = await Add(AccountType.Expense, "Rent");

        var descendants = await CategoryWriteHelpers.GetDescendantIdsAsync(_db, food.Id, Ct);

        Assert.Contains(groceries.Id, descendants);
        Assert.Contains(produce.Id, descendants);
        Assert.DoesNotContain(food.Id, descendants);   // excludes self
        Assert.DoesNotContain(rent.Id, descendants);   // unrelated root
    }
}
