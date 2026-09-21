using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CreateConsumable;

public class CreateConsumableHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IStockService stock,
    IInventoryTagPathResolver tagResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateConsumableCommand, ConsumableResponse>
{
    public async Task<ConsumableResponse> Handle(CreateConsumableCommand request, CancellationToken ct)
    {
        if (request.BrandId is { } brandId)
        {
            var brandExists = await db.Brands.AnyAsync(b => b.Id == brandId, ct);
            if (!brandExists)
                throw new BusinessRuleException("BrandId must reference an existing brand.");
        }

        var item = new Item
        {
            Name = request.Name.Trim(),
            Fungibility = Fungibility.Fungible,
            Status = ItemStatus.Active,
            Unit = Normalize(request.Unit),
            RestockThreshold = request.RestockThreshold,
            BrandId = request.BrandId,
            Notes = Normalize(request.Notes),
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

        // The item must exist before ReceiveAsync's lookups (LoadFungibleItemAsync reads db.Items);
        // this mid-handler flush opens the one command transaction lazily and gives read-your-writes.
        await unitOfWork.SaveChangesAsync(ct);

        // Optionally seed starting stock at the drilled location, in the same transaction. If this
        // throws (e.g. nonexistent area, a container that isn't a container), the whole command rolls
        // back — no orphaned bare consumable.
        if (request.InitialQuantity is { } qty && qty > 0m)
        {
            var to = new StockLocation(request.InitialAreaId, request.InitialContainerItemId);
            await stock.ReceiveAsync(item.Id,
                new StockReceiveRequest(to, qty, UnitCost: null, UnitCostCurrencyId: null,
                    OccurredAt: null, Note: null, Source: null), ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return await db.Items
            .AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(ConsumableResponseMapper.Projection)
            .FirstAsync(ct);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
