using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public interface ISystemAccountProvider
{
    Task<Account> GetOpeningBalancesAsync(CancellationToken ct);
}
