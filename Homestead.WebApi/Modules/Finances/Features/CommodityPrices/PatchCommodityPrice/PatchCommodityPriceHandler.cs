using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.PatchCommodityPrice;

public class PatchCommodityPriceHandler(
    AppDbContext db,
    IAuditContext audit,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<PatchCommodityPriceCommand, CommodityPriceResponse>
{
    public async Task<CommodityPriceResponse> Handle(
        PatchCommodityPriceCommand request, CancellationToken ct)
    {
        var price = await db.CommodityPrices.FirstOrDefaultAsync(p => p.Id == request.Id, ct);
        if (price is null)
            throw new EntityNotFoundException("Commodity price not found.");

        if (price.CreatedByTransactionId is not null)
            throw new BusinessRuleException(
                "Transaction-linked prices are edited through their source transaction.");

        audit.Track<CommodityPriceSnapshot, CommodityPrice>(price, "CommodityPrice", price.Id);

        var model = new PatchCommodityPriceModel
        {
            Rate = price.Rate,
        };

        request.PatchDocument.ApplyOrThrow(model);

        if (model.Rate <= 0m)
            throw new BusinessRuleException("Rate must be greater than zero.");

        price.Rate = model.Rate;
        price.UpdatedAt = DateTimeOffset.UtcNow;
        price.UpdatedByUserId = currentUser.UserId;

        await db.SaveChangesAsync(ct);

        return new CommodityPriceResponse
        {
            Id = price.Id,
            CommodityId = price.CommodityId,
            QuoteCurrencyId = price.QuoteCurrencyId,
            PriceAt = price.PriceAt,
            Rate = price.Rate,
            CreatedByTransactionId = price.CreatedByTransactionId,
        };
    }
}
