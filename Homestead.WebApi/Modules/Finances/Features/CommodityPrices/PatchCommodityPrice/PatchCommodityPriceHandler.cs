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
            PriceAt = price.PriceAt,
            Rate = price.Rate,
        };

        request.PatchDocument.ApplyOrThrow(model);

        if (model.Rate <= 0m)
            throw new BusinessRuleException("Rate must be greater than zero.");

        if (model.PriceAt != price.PriceAt)
        {
            if (model.PriceAt > DateTimeOffset.UtcNow.AddDays(1))
                throw new BusinessRuleException("PriceAt cannot be more than one day in the future.");

            var newPriceAt = model.PriceAt.ToUniversalTime();

            var collision = await db.CommodityPrices
                .AsNoTracking()
                .Where(p => p.Id != price.Id
                         && p.CommodityId == price.CommodityId
                         && p.QuoteCurrencyId == price.QuoteCurrencyId
                         && p.PriceAt == newPriceAt)
                .Select(p => new { p.CreatedByTransactionId })
                .FirstOrDefaultAsync(ct);

            if (collision is not null)
            {
                if (collision.CreatedByTransactionId is Guid txnId)
                    throw new BusinessRuleException(
                        $"A transaction-linked price already exists at this instant (transaction {txnId}). "
                        + "Edit the source transaction instead.");

                throw new DuplicateEntityException(
                    "A price for this commodity, quote currency, and instant already exists.");
            }

            price.PriceAt = newPriceAt;
        }

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
