using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.PatchConsumable;

public class PatchConsumableHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchConsumableCommand, ConsumableResponse>
{
    // A consumable uses only the reduced fungible lifecycle; lending or repairing a count is meaningless.
    private static readonly ItemStatus[] FungibleStatuses = [ItemStatus.Active, ItemStatus.Archived];

    public async Task<ConsumableResponse> Handle(PatchConsumableCommand request, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == request.Id, ct);
        if (item is null)
            throw new EntityNotFoundException("Consumable not found.");
        if (item.Fungibility != Fungibility.Fungible)
            throw new BusinessRuleException("Only fungible consumables can be edited here.");

        audit.Track<ItemSnapshot, Item>(item, "Item", item.Id);

        var model = new PatchConsumableModel
        {
            Name = item.Name,
            Unit = item.Unit,
            RestockThreshold = item.RestockThreshold,
            BrandId = item.BrandId,
            Notes = item.Notes,
            Status = item.Status,
        };

        request.PatchDocument.ApplyOrThrow(model);

        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Name is required.");
        if (model.Name.Length > 256)
            throw new BusinessRuleException("Name must be 256 characters or fewer.");
        if (model.Unit?.Length > 32)
            throw new BusinessRuleException("Unit must be 32 characters or fewer.");
        if (model.RestockThreshold is < 0m)
            throw new BusinessRuleException("Restock threshold must be zero or positive.");
        // Also the B2 guard for consumables: JsonPatch can bind an out-of-range integer to the
        // enum-typed Status (allowIntegerValues defaults true), but this Active/Archived allowlist
        // already rejects any undefined value before it can persist and re-serialize as a JSON number
        // — no separate Enum.IsDefined check needed here (unlike PatchDurable, which has no allowlist).
        if (!FungibleStatuses.Contains(model.Status))
            throw new BusinessRuleException("A consumable can only be Active or Archived.");

        if (model.BrandId is { } brandId && brandId != item.BrandId)
        {
            var brandExists = await db.Brands.AnyAsync(b => b.Id == brandId, ct);
            if (!brandExists)
                throw new BusinessRuleException("BrandId must reference an existing brand.");
        }

        item.Name = model.Name.Trim();
        item.Unit = Normalize(model.Unit);
        item.RestockThreshold = model.RestockThreshold;
        item.BrandId = model.BrandId;
        item.Notes = Normalize(model.Notes);
        item.Status = model.Status;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedByUserId = currentUser.UserId;

        await unitOfWork.SaveChangesAsync(ct);

        return await db.Items
            .AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(ConsumableResponseMapper.Projection)
            .FirstAsync(ct);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
