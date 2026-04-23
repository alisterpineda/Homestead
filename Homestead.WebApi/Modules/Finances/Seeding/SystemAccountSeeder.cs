using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Seeding;

public static class SystemAccountSeeder
{
    public const string OpeningBalancesName = "Equity:Opening Balances";
    public const string AdjustmentsName = "Equity:Adjustments";

    // Stored default only; user-facing balances always resolve through the chosen DisplayCurrency.
    private const string DefaultCommoditySymbol = "USD";

    private static readonly IReadOnlyList<string> RequiredSystemAccounts =
        [OpeningBalancesName, AdjustmentsName];

    public static void Seed(DbContext context, bool _ = false)
    {
        var accounts = context.Set<Account>();
        var existingNames = accounts
            .Where(a => a.IsSystem)
            .Select(a => a.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = RequiredSystemAccounts.Where(n => !existingNames.Contains(n)).ToList();
        if (missing.Count == 0) return;

        var defaultCommodityId = context.Set<Commodity>()
            .Where(c => c.Symbol == DefaultCommoditySymbol && c.Kind == CommodityKind.Currency)
            .Select(c => c.Id)
            .FirstOrDefault();

        if (defaultCommodityId == Guid.Empty) return;

        accounts.AddRange(BuildMissing(missing, defaultCommodityId));
        context.SaveChanges();
    }

    public static async Task SeedAsync(DbContext context, bool _ = false, CancellationToken cancellationToken = default)
    {
        var accounts = context.Set<Account>();
        var existingNames = (await accounts
            .Where(a => a.IsSystem)
            .Select(a => a.Name)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var missing = RequiredSystemAccounts.Where(n => !existingNames.Contains(n)).ToList();
        if (missing.Count == 0) return;

        var defaultCommodityId = await context.Set<Commodity>()
            .Where(c => c.Symbol == DefaultCommoditySymbol && c.Kind == CommodityKind.Currency)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultCommodityId == Guid.Empty) return;

        await accounts.AddRangeAsync(BuildMissing(missing, defaultCommodityId), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static List<Account> BuildMissing(IEnumerable<string> missingNames, Guid defaultCommodityId)
    {
        var toAdd = new List<Account>();
        foreach (var name in missingNames)
        {
            toAdd.Add(new Account
            {
                Id = Guid.NewGuid(),
                Name = name,
                AccountType = AccountType.Equity,
                DefaultCommodityId = defaultCommodityId,
                IsMultiPosition = true,
                IsSystem = true,
                IsArchived = false,
            });
        }
        return toAdd;
    }
}
