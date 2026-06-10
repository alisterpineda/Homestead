using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.PatchItem;

public class PatchItemHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchItemCommand, ItemResponse>
{
    public async Task<ItemResponse> Handle(PatchItemCommand request, CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == request.Id, ct);
        if (item is null)
            throw new EntityNotFoundException("Item not found.");

        // v1 surfaces non-fungible items only.
        if (item.Fungibility != Fungibility.NonFungible)
            throw new BusinessRuleException("Only non-fungible items can be edited.");

        audit.Track<ItemSnapshot, Item>(item, "Item", item.Id);

        var model = new PatchItemModel
        {
            Name = item.Name,
            Notes = item.Notes,
            Serial = item.Serial,
            AcquiredOn = item.AcquiredOn,
            Value = item.Value,
            ValueCurrencyId = item.ValueCurrencyId,
            Status = item.Status,
            LentTo = item.LentTo,
            AreaId = item.AreaId,
        };

        request.PatchDocument.ApplyOrThrow(model);

        if (string.IsNullOrWhiteSpace(model.Name))
            throw new BusinessRuleException("Name is required.");
        if (model.Name.Length > 256)
            throw new BusinessRuleException("Name must be 256 characters or fewer.");
        if (model.Serial is { Length: > 128 })
            throw new BusinessRuleException("Serial must be 128 characters or fewer.");
        if (model.LentTo is { Length: > 256 })
            throw new BusinessRuleException("LentTo must be 256 characters or fewer.");

        if (model.AcquiredOn is { } acquired && DateOnlyValidationRules.IsInFuture(acquired))
            throw new BusinessRuleException("AcquiredOn cannot be in the future.");

        // Value and its currency are set together or not at all.
        if (model.Value.HasValue != model.ValueCurrencyId.HasValue)
            throw new BusinessRuleException("Value and ValueCurrencyId must be set together.");
        if (model.Value is < 0)
            throw new BusinessRuleException("Value must be zero or positive.");
        if (model.ValueCurrencyId is { } currencyId)
        {
            var currency = await db.Commodities.FirstOrDefaultAsync(c => c.Id == currencyId, ct);
            if (currency is null)
                throw new BusinessRuleException("ValueCurrencyId must reference an existing commodity.");
            if (currency.Kind != CommodityKind.Currency)
                throw new BusinessRuleException("ValueCurrencyId must reference a currency commodity.");
        }

        // Lent requires a recipient; any other status clears the LentTo note.
        string? lentTo;
        if (model.Status == ItemStatus.Lent)
        {
            if (string.IsNullOrWhiteSpace(model.LentTo))
                throw new BusinessRuleException("LentTo is required when the status is Lent.");
            lentTo = model.LentTo.Trim();
        }
        else
        {
            lentTo = null;
        }

        // Terminal statuses keep their last location: ignore any AreaId change in the same patch.
        var targetAreaId = model.Status.IsTerminal() ? item.AreaId : model.AreaId;
        if (targetAreaId is { } areaId && areaId != item.AreaId)
        {
            var areaExists = await db.Areas.AnyAsync(a => a.Id == areaId, ct);
            if (!areaExists)
                throw new BusinessRuleException("AreaId must reference an existing area.");
        }

        item.Name = model.Name.Trim();
        item.Notes = Normalize(model.Notes);
        item.Serial = Normalize(model.Serial);
        item.AcquiredOn = model.AcquiredOn;
        item.Value = model.Value;
        item.ValueCurrencyId = model.ValueCurrencyId;
        item.Status = model.Status;
        item.LentTo = lentTo;
        item.AreaId = targetAreaId;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedByUserId = currentUser.UserId;

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
