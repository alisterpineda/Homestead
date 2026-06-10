using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.CreateItem;

public class CreateItemHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateItemCommand, ItemResponse>
{
    public async Task<ItemResponse> Handle(CreateItemCommand request, CancellationToken ct)
    {
        if (request.AreaId is { } areaId)
        {
            var areaExists = await db.Areas.AnyAsync(a => a.Id == areaId, ct);
            if (!areaExists)
                throw new BusinessRuleException("AreaId must reference an existing area.");
        }

        // Value and its currency are set together or not at all.
        if (request.Value.HasValue != request.ValueCurrencyId.HasValue)
            throw new BusinessRuleException("Value and ValueCurrencyId must be set together.");

        if (request.ValueCurrencyId is { } currencyId)
        {
            var currency = await db.Commodities.FirstOrDefaultAsync(c => c.Id == currencyId, ct);
            if (currency is null)
                throw new BusinessRuleException("ValueCurrencyId must reference an existing commodity.");
            if (currency.Kind != CommodityKind.Currency)
                throw new BusinessRuleException("ValueCurrencyId must reference a currency commodity.");
        }

        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            IsContainer = false,
            Notes = Normalize(request.Notes),
            Serial = Normalize(request.Serial),
            AcquiredOn = request.AcquiredOn,
            Value = request.Value,
            ValueCurrencyId = request.ValueCurrencyId,
            AreaId = request.AreaId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        db.Items.Add(item);
        audit.Created("Item", item.Id);

        await unitOfWork.SaveChangesAsync(ct);

        return await db.Items
            .AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(ItemResponseMapper.Projection)
            .FirstAsync(ct);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
