using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Durables;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.TrackDurable;

public class TrackDurableHandler(
    AppDbContext db,
    IStockService stock,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IItemLocationResolver locationResolver,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TrackDurableCommand, TrackDurableResponse>
{
    public async Task<TrackDurableResponse> Handle(TrackDurableCommand request, CancellationToken ct)
    {
        var body = request.Body;
        var now = DateTimeOffset.UtcNow;

        // The source container also becomes the new item's parent, so it must be live. ConvertOutAsync
        // validates the source only for existence + container-kind (a terminal source is allowed so
        // stranded stock can still be carved out) — it never proved liveness — so gate the parent here:
        // things move OUT of a terminal holder but never INTO one (the complement of the sealing gate).
        if (body.ContainerItemId is { } containerId)
        {
            var container = await db.Items.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == containerId, ct);
            if (container is not null && container.Status.IsTerminal())
                throw new BusinessRuleException("ContainerItemId must reference a live container.");
        }

        // The new item carries a client-side Guid so its id is known before insert — the conversion
        // movement's RelatedItemId can point at it within the same transaction. The source location
        // becomes the item's location.
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = body.Name.Trim(),
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            DurableKind = DurableKind.Plain,
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

        await stock.ConvertOutAsync(request.ConsumableId, new StockConvertOutRequest(
            new StockLocation(body.AreaId, body.ContainerItemId),
            item.Id, body.OccurredAt, body.Note), ct);

        await unitOfWork.SaveChangesAsync(ct);

        var itemResponse = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(DurableResponseMapper.Projection)
            .FirstAsync(ct);
        itemResponse = await locationResolver.EnrichAsync(itemResponse, ct);

        var detail = await ConsumableDetailReader.LoadAsync(db, locationResolver, request.ConsumableId, ct)
            ?? throw new EntityNotFoundException("Consumable not found.");

        return new TrackDurableResponse { Detail = detail, Item = itemResponse };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
