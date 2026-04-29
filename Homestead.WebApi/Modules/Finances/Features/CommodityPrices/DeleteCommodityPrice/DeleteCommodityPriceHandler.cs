using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrice;

public class DeleteCommodityPriceHandler(AppDbContext db, IAuditContext audit)
    : IRequestHandler<DeleteCommodityPriceCommand, bool>
{
    public async Task<bool> Handle(DeleteCommodityPriceCommand request, CancellationToken ct)
    {
        var price = await db.CommodityPrices.FirstOrDefaultAsync(p => p.Id == request.Id, ct);
        if (price is null)
            return false;

        price.Source.RequireManual("deleted");

        db.CommodityPrices.Remove(price);
        audit.Deleted("CommodityPrice", price.Id);
        await db.SaveChangesAsync(ct);

        return true;
    }
}
