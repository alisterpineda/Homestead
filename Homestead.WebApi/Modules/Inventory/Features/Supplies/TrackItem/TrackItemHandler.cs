using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies.TrackItem;

public class TrackItemHandler(
    AppDbContext db,
    IStockService stock,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TrackItemCommand, TrackItemResponse>
{
    public async Task<TrackItemResponse> Handle(TrackItemCommand request, CancellationToken ct)
    {
        var body = request.Body;
        var now = DateTimeOffset.UtcNow;

        // The new item carries a client-side Guid so its id is known before insert — the conversion
        // movement's RelatedItemId can point at it within the same transaction. The source location
        // becomes the item's location; ConvertOutAsync's location validation proves a container is
        // live, so no separate container check is needed here.
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = body.Name.Trim(),
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            IsContainer = false,
            AreaId = body.AreaId,
            ParentItemId = body.ContainerItemId,
            // Compartment is only meaningful inside a container.
            Compartment = body.ContainerItemId is null ? null : Normalize(body.Compartment),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        db.Items.Add(item);
        audit.Created("Item", item.Id);

        await stock.ConvertOutAsync(request.SupplyId, new StockConvertOutRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            item.Id, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        var itemResponse = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(ItemResponseMapper.Projection)
            .FirstAsync(ct);
        itemResponse = await locationResolver.EnrichAsync(itemResponse, ct);

        var detail = await SupplyDetailReader.LoadAsync(db, locationResolver, request.SupplyId, ct)
            ?? throw new EntityNotFoundException("Supply not found.");

        return new TrackItemResponse { Detail = detail, Item = itemResponse };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
