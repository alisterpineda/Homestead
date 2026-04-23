using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class SystemAccountProvider(AppDbContext db) : ISystemAccountProvider
{
    private Account? _openingBalances;

    public async Task<Account> GetOpeningBalancesAsync(CancellationToken ct)
    {
        _openingBalances ??= await db.Accounts
            .FirstOrDefaultAsync(a => a.IsSystem && a.Name == SystemAccountSeeder.OpeningBalancesName, ct)
            ?? throw new BusinessRuleException(
                $"System account '{SystemAccountSeeder.OpeningBalancesName}' is missing. Run the finances seeder.");
        return _openingBalances;
    }
}
