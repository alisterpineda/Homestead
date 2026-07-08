using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.CreateDurable;

public class CreateDurableHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IItemLocationResolver locationResolver,
    IInventoryTagPathResolver tagResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateDurableCommand, DurableResponse>
{
    public async Task<DurableResponse> Handle(CreateDurableCommand request, CancellationToken ct)
    {
        if (request.AreaId is { } areaId)
        {
            var areaExists = await db.Areas.AnyAsync(a => a.Id == areaId, ct);
            if (!areaExists)
                throw new BusinessRuleException("AreaId must reference an existing area.");
        }

        // A parent must be a non-fungible holder (container or assembly). No cycle is possible on
        // create — the new row has no descendants yet.
        if (request.ParentItemId is { } parentId)
        {
            var parent = await db.Items.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == parentId, ct);
            if (parent is null)
                throw new BusinessRuleException("ParentItemId must reference an existing item.");
            if (parent.Fungibility != Fungibility.NonFungible || !parent.DurableKind.IsHolder())
                throw new BusinessRuleException("ParentItemId must reference a container or assembly item.");
            // Things move OUT of a terminal (Sold/Discarded/Archived) holder but never INTO one — the
            // complement of the sealing gate. A new item must be parented into a live holder.
            if (parent.Status.IsTerminal())
                throw new BusinessRuleException("ParentItemId must reference a live container or assembly.");
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

        if (request.BrandId is { } brandId)
        {
            var brandExists = await db.Brands.AnyAsync(b => b.Id == brandId, ct);
            if (!brandExists)
                throw new BusinessRuleException("BrandId must reference an existing brand.");
        }

        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            DurableKind = request.DurableKind,
            Notes = Normalize(request.Notes),
            Serial = Normalize(request.Serial),
            BrandId = request.BrandId,
            Model = Normalize(request.Model),
            AcquiredOn = request.AcquiredOn,
            Value = request.Value,
            ValueCurrencyId = request.ValueCurrencyId,
            AreaId = request.AreaId,
            ParentItemId = request.ParentItemId,
            // Compartment is only meaningful inside a container; the validator already rejects it
            // without a parent, so null it out defensively when there is none.
            Compartment = request.ParentItemId is null ? null : Normalize(request.Compartment),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        db.Items.Add(item);

        var tagPaths = request.Tags?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [];
        var tags = await tagResolver.ResolveOrCreateManyAsync(tagPaths, currentUser.UserId, ct);
        InventoryItemTagSync.SetItemTags(item, tags);

        audit.Created("Item", item.Id);

        await unitOfWork.SaveChangesAsync(ct);

        var response = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(DurableResponseMapper.Projection)
            .FirstAsync(ct);

        return await locationResolver.EnrichAsync(response, ct);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
