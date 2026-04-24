using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.CreateCommodityPrice;

public class CreateCommodityPriceHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit)
    : IRequestHandler<CreateCommodityPriceCommand, CommodityPriceResponse>
{
    public async Task<CommodityPriceResponse> Handle(
        CreateCommodityPriceCommand request, CancellationToken ct)
    {
        var commodities = await db.Commodities
            .AsNoTracking()
            .Where(c => c.Id == request.CommodityId || c.Id == request.QuoteCurrencyId)
            .Select(c => new { c.Id, c.Kind })
            .ToListAsync(ct);

        if (commodities.All(c => c.Id != request.CommodityId))
            throw new BusinessRuleException("CommodityId must reference an existing commodity.");

        var quote = commodities.FirstOrDefault(c => c.Id == request.QuoteCurrencyId);
        if (quote is null)
            throw new BusinessRuleException("QuoteCurrencyId must reference an existing commodity.");
        if (quote.Kind != CommodityKind.Currency)
            throw new BusinessRuleException("QuoteCurrencyId must reference a currency commodity.");

        if (request.PriceAt > DateTimeOffset.UtcNow.AddDays(1))
            throw new BusinessRuleException("PriceAt cannot be more than one day in the future.");

        var priceAt = request.PriceAt.ToUniversalTime();

        var existing = await db.CommodityPrices
            .AsNoTracking()
            .Where(p => p.CommodityId == request.CommodityId
                     && p.QuoteCurrencyId == request.QuoteCurrencyId
                     && p.PriceAt == priceAt)
            .Select(p => new { p.Id, p.CreatedByTransactionId })
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            if (existing.CreatedByTransactionId is Guid txnId)
                throw new BusinessRuleException(
                    $"A transaction-linked price already exists at this instant (transaction {txnId}). "
                    + "Edit the source transaction instead.");

            throw new DuplicateEntityException(
                "A price for this commodity, quote currency, and instant already exists.");
        }

        var price = new CommodityPrice
        {
            Id = Guid.NewGuid(),
            CommodityId = request.CommodityId,
            QuoteCurrencyId = request.QuoteCurrencyId,
            PriceAt = priceAt,
            Rate = request.Rate,
            CreatedByTransactionId = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        db.CommodityPrices.Add(price);
        audit.Created("CommodityPrice", price.Id);
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
