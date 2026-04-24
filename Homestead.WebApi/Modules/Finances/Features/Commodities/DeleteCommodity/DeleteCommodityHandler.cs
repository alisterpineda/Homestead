using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.DeleteCommodity;

public class DeleteCommodityHandler(AppDbContext db, IAuditContext audit)
    : IRequestHandler<DeleteCommodityCommand, bool>
{
    public async Task<bool> Handle(DeleteCommodityCommand request, CancellationToken ct)
    {
        var commodity = await db.Commodities.FirstOrDefaultAsync(c => c.Id == request.Id, ct);
        if (commodity is null)
            return false;

        if (commodity.IsSystemManaged)
            throw new BusinessRuleException("Seeded currencies cannot be deleted.");

        var refs = await db.Commodities
            .Where(c => c.Id == request.Id)
            .Select(c => new
            {
                ByAccount = db.Accounts.Any(a => a.ReportingCurrencyId == c.Id),
                ByTransactionCurrency = db.AccountTransactions.Any(t => t.CurrencyId == c.Id),
                BySplit = db.AccountTransactionSplits.Any(s => s.CommodityId == c.Id),
                ByPrice = db.CommodityPrices.Any(p => p.CommodityId == c.Id || p.QuoteCurrencyId == c.Id),
            })
            .SingleAsync(ct);

        if (refs.ByAccount)
            throw new BusinessRuleException("Commodity is in use as an account's reporting currency.");
        if (refs.ByTransactionCurrency)
            throw new BusinessRuleException("Commodity is in use as a transaction currency.");
        if (refs.BySplit)
            throw new BusinessRuleException("Commodity is in use on a transaction split.");
        if (refs.ByPrice)
            throw new BusinessRuleException("Commodity is referenced by a price entry.");

        db.Commodities.Remove(commodity);
        audit.Deleted("Commodity", commodity.Id, $"{commodity.Symbol} — {commodity.Name}");
        await db.SaveChangesAsync(ct);

        return true;
    }
}
