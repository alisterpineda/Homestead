using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface ISystemAccountProvider
{
    Task<Account> GetOpeningBalancesAsync(CancellationToken ct);
    Task<Account> GetAdjustmentsAsync(CancellationToken ct);
    Task<Account> GetIncomeAsync(CancellationToken ct);
    Task<Account> GetExpenseAsync(CancellationToken ct);
}
