using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Services;

public class SystemAccountProvider(AppDbContext db) : ISystemAccountProvider
{
    private readonly Dictionary<string, Account> _cache = new(StringComparer.Ordinal);

    public Task<Account> GetOpeningBalancesAsync(CancellationToken ct) =>
        GetByNameAsync(SystemAccountSeeder.OpeningBalancesName, ct);

    public Task<Account> GetAdjustmentsAsync(CancellationToken ct) =>
        GetByNameAsync(SystemAccountSeeder.AdjustmentsName, ct);

    public Task<Account> GetIncomeAsync(CancellationToken ct) =>
        GetByNameAsync(SystemAccountSeeder.IncomeName, ct);

    public Task<Account> GetExpenseAsync(CancellationToken ct) =>
        GetByNameAsync(SystemAccountSeeder.ExpenseName, ct);

    private async Task<Account> GetByNameAsync(string name, CancellationToken ct)
    {
        if (_cache.TryGetValue(name, out var cached))
            return cached;

        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.IsSystem && a.Name == name, ct)
            ?? throw new BusinessRuleException(
                $"System account '{name}' is missing. Run the finances seeder.");

        _cache[name] = account;
        return account;
    }
}
