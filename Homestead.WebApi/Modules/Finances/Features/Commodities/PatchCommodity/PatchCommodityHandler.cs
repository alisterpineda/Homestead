using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.PatchCommodity;

public class PatchCommodityHandler(
    AppDbContext db,
    IAuditContext audit,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<PatchCommodityCommand, CommodityResponse>
{
    public async Task<CommodityResponse> Handle(PatchCommodityCommand request, CancellationToken ct)
    {
        var commodity = await db.Commodities.FirstOrDefaultAsync(c => c.Id == request.Id, ct);
        if (commodity is null)
            throw new EntityNotFoundException("Commodity not found.");

        if (commodity.IsSystemManaged)
            throw new BusinessRuleException("Seeded currencies are read-only.");

        audit.Track<CommoditySnapshot, Commodity>(commodity, "Commodity", commodity.Id);

        var model = new PatchCommodityModel
        {
            Name = commodity.Name,
            DisplayPrecision = commodity.DisplayPrecision,
        };

        request.PatchDocument.ApplyOrThrow(model);

        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Name is required.");
        if (model.Name.Length > 256)
            throw new BusinessRuleException("Name must be 256 characters or fewer.");
        if (model.DisplayPrecision is < 0 or > 10)
            throw new BusinessRuleException("DisplayPrecision must be between 0 and 10.");

        commodity.Name = model.Name.Trim();
        commodity.DisplayPrecision = model.DisplayPrecision;
        commodity.UpdatedAt = DateTimeOffset.UtcNow;
        commodity.UpdatedByUserId = currentUser.UserId;

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
