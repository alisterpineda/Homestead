using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.CreateCommodity;

public class CreateCommodityHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit)
    : IRequestHandler<CreateCommodityCommand, CommodityResponse>
{
    public async Task<CommodityResponse> Handle(CreateCommodityCommand request, CancellationToken ct)
    {
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var exchange = request.Exchange.Trim().ToUpperInvariant();
        var name = request.Name.Trim();

        var exists = await db.Commodities.AnyAsync(c =>
            c.Symbol == symbol && c.Exchange == exchange, ct);
        if (exists)
            throw new DuplicateEntityException(
                $"A security with symbol '{symbol}' on exchange '{exchange}' already exists.");

        var commodity = new Commodity
        {
            Id = Guid.NewGuid(),
            Symbol = symbol,
            Exchange = exchange,
            Name = name,
            Kind = CommodityKind.Security,
            DisplayPrecision = request.DisplayPrecision,
            IsSystemManaged = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        db.Commodities.Add(commodity);
        audit.Created("Commodity", commodity.Id);
        await db.SaveChangesAsync(ct);

        return new CommodityResponse
        {
            Id = commodity.Id,
            Symbol = commodity.Symbol,
            Exchange = commodity.Exchange,
            Name = commodity.Name,
            Kind = commodity.Kind,
            DisplayPrecision = commodity.DisplayPrecision,
            IsSystemManaged = commodity.IsSystemManaged,
        };
    }
}
