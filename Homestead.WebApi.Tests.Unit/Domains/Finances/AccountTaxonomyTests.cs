using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class AccountTaxonomyTests
{
    [Theory]
    [InlineData(AccountType.Asset, true)]
    [InlineData(AccountType.Liability, true)]
    [InlineData(AccountType.Equity, false)]
    [InlineData(AccountType.Income, false)]
    [InlineData(AccountType.Expense, false)]
    public void IsBalanceSheet_MatchesAssetAndLiabilityOnly(AccountType type, bool expected)
    {
        Assert.Equal(expected, type.IsBalanceSheet());
    }

    [Theory]
    [InlineData(AccountType.Income, true)]
    [InlineData(AccountType.Expense, true)]
    [InlineData(AccountType.Asset, false)]
    [InlineData(AccountType.Liability, false)]
    [InlineData(AccountType.Equity, false)]
    public void IsCategory_MatchesIncomeAndExpenseOnly(AccountType type, bool expected)
    {
        Assert.Equal(expected, type.IsCategory());
    }

    [Theory]
    [InlineData(AccountType.Asset, true, false)]
    [InlineData(AccountType.Liability, true, false)]
    [InlineData(AccountType.Equity, false, false)]
    [InlineData(AccountType.Income, false, true)]
    [InlineData(AccountType.Expense, false, true)]
    public void AccountQueries_Predicates_AgreeWithScalarHelpers(
        AccountType type, bool isBalanceSheet, bool isCategory)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(), Name = "x", AccountType = type,
            ReportingCurrencyId = Guid.NewGuid(),
        };

        var balanceSheet = AccountQueries.IsBalanceSheetAccount.Compile();
        var category = AccountQueries.IsCategoryAccount.Compile();

        Assert.Equal(isBalanceSheet, balanceSheet(account));
        Assert.Equal(isCategory, category(account));
        // One rule, two shapes — the EF predicate and the scalar helper must never disagree.
        Assert.Equal(type.IsBalanceSheet(), balanceSheet(account));
        Assert.Equal(type.IsCategory(), category(account));
    }
}
