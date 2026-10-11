using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.PatchCommodityPrice;

public class PatchCommodityPriceHandler(
    AppDbContext db,
    IAuditContext audit,
    ICurrentUserAccessor currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<CommodityPriceResponse> HandleAsync(
        Guid id, JsonPatchDocument<PatchCommodityPriceModel> patchDoc, CancellationToken ct)
    {
        var price = await db.CommodityPrices.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (price is null)
            throw new EntityNotFoundException("Commodity price not found.");

        price.Source.RequireManual("edited");

        audit.Track<CommodityPriceSnapshot, CommodityPrice>(price, "CommodityPrice", price.Id);

        var model = new PatchCommodityPriceModel
        {
            PriceAt = price.PriceAt,
            Rate = price.Rate,
        };

        patchDoc.ApplyOrThrow(model);

        if (model.Rate <= 0m)
            throw new BusinessRuleException("Rate must be greater than zero.");

        if (model.PriceAt != price.PriceAt)
        {
            if (model.PriceAt > DateTimeOffset.UtcNow.AddDays(1))
                throw new BusinessRuleException("PriceAt cannot be more than one day in the future.");

            var newPriceAt = model.PriceAt.ToUniversalTime();

            var collision = await db.CommodityPrices
                .AsNoTracking()
                .AnyAsync(p => p.Id != price.Id
                            && p.CommodityId == price.CommodityId
                            && p.QuoteCurrencyId == price.QuoteCurrencyId
                            && p.PriceAt == newPriceAt
                            && p.Source == price.Source, ct);

            if (collision)
                throw new DuplicateEntityException(
                    "A price for this commodity, quote currency, and instant already exists.");

            price.PriceAt = newPriceAt;
        }

        price.Rate = model.Rate;
        price.UpdatedAt = DateTimeOffset.UtcNow;
        price.UpdatedByUserId = currentUser.UserId;

        await unitOfWork.SaveChangesAsync(ct);

        return new CommodityPriceResponse
        {
            Id = price.Id,
            CommodityId = price.CommodityId,
            QuoteCurrencyId = price.QuoteCurrencyId,
            PriceAt = price.PriceAt,
            Rate = price.Rate,
            Source = price.Source,
            CreatedByTransactionId = price.CreatedByTransactionId,
        };
    }
}
