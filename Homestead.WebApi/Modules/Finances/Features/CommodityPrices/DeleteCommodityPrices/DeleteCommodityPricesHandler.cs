using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrices;

public class DeleteCommodityPricesHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCommodityPricesCommand, DeleteCommodityPricesResponse>
{
    public async Task<DeleteCommodityPricesResponse> Handle(
        DeleteCommodityPricesCommand request, CancellationToken ct)
    {
        var prices = await db.CommodityPrices
            .Where(p => request.Ids.Contains(p.Id))
            .ToListAsync(ct);

        if (prices.Count == 0)
            return new DeleteCommodityPricesResponse(0);

        foreach (var price in prices)
            price.Source.RequireDeletable();

        db.CommodityPrices.RemoveRange(prices);
        foreach (var price in prices)
            audit.Deleted("CommodityPrice", price.Id);

        await unitOfWork.SaveChangesAsync(ct);
        return new DeleteCommodityPricesResponse(prices.Count);
    }
}
