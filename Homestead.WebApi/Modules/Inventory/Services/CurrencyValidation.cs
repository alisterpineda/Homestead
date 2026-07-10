using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// The shared "must reference an existing currency commodity" check behind every money+currency
/// pair in the module (item value, acquisition cost, stock unit cost). Field-specific wording is
/// built from <paramref name="fieldName"/> so each pair keeps its historical message.
/// </summary>
internal static class CurrencyValidation
{
    public static async Task EnsureCurrencyAsync(
        AppDbContext db, Guid currencyId, string fieldName, CancellationToken ct)
    {
        var kind = await db.Commodities
            .Where(c => c.Id == currencyId)
            .Select(c => (CommodityKind?)c.Kind)
            .FirstOrDefaultAsync(ct);
        if (kind is null)
            throw new BusinessRuleException($"{fieldName} must reference an existing commodity.");
        if (kind != CommodityKind.Currency)
            throw new BusinessRuleException($"{fieldName} must reference a currency commodity.");
    }
}
