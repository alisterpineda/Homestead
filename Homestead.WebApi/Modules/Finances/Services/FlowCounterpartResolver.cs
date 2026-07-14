using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public class FlowCounterpartResolver(ISystemAccountProvider systemAccounts) : IFlowCounterpartResolver
{
    // Phase 0 ignores categoryHint — spend/income is uncategorised capture, posting to the seeded
    // system Expense/Income accounts. Do NOT writability-check the result: like BalanceEntryService
    // posting to Equity:Adjustments and DividendService posting to Income, the system counterpart is
    // deliberately a non-user-writable account.
    public Task<Account> ResolveAsync(FlowDirection direction, Guid? categoryHint, CancellationToken ct) =>
        direction == FlowDirection.Expense
            ? systemAccounts.GetExpenseAsync(ct)
            : systemAccounts.GetIncomeAsync(ct);
}
