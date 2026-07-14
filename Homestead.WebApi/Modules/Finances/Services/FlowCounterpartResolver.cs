using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class FlowCounterpartResolver(AppDbContext db, ISystemAccountProvider systemAccounts)
    : IFlowCounterpartResolver
{
    // Do NOT writability-check the result: like BalanceEntryService posting to Equity:Adjustments and
    // DividendService posting to Income, the counterpart is deliberately a non-user-writable account.
    // A category is a counterpart too — the validation below (type/direction/archived) replaces the
    // writability check.
    public async Task<Account> ResolveAsync(FlowDirection direction, Guid? categoryHint, CancellationToken ct)
    {
        // null → uncategorised capture: post to the seeded system Expense/Income bucket by direction.
        if (categoryHint is not Guid categoryId)
            return direction == FlowDirection.Expense
                ? await systemAccounts.GetExpenseAsync(ct)
                : await systemAccounts.GetIncomeAsync(ct);

        var category = await db.Accounts.FindAsync([categoryId], ct)
            ?? throw new BusinessRuleException("Category not found.");

        if (!category.AccountType.IsCategory())
            throw new BusinessRuleException($"'{category.Name}' is not a category account.");

        var expectedType = direction == FlowDirection.Expense ? AccountType.Expense : AccountType.Income;
        if (category.AccountType != expectedType)
            throw new BusinessRuleException(
                $"A {direction} flow requires a {expectedType} category (got {category.AccountType}).");

        if (category.IsArchived)
            throw new BusinessRuleException($"Category '{category.Name}' is archived.");

        return category;
    }
}
