using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.CreateWarranty;

public class CreateItemWarrantyHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IValidator<CreateItemWarrantyCommand> validator)
{
    public async Task<ItemWarrantyResponse> Handle(CreateItemWarrantyCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var item = await db.Items.AsNoTracking()
            .Where(i => i.Id == request.ItemId)
            .Select(i => new { i.Id, i.Fungibility })
            .FirstOrDefaultAsync(ct);
        if (item is null)
            throw new EntityNotFoundException("Item not found.");

        // Durables only. The rule spans tables (warranty row → item kind), so it can't be a check
        // constraint — same posture as ParentItemId's placement rules.
        if (item.Fungibility != Fungibility.NonFungible)
            throw new BusinessRuleException("Warranties apply to durable items only.");

        var now = DateTimeOffset.UtcNow;
        var warranty = new ItemWarranty
        {
            ItemId = item.Id,
            Provider = ItemWarrantyText.NormalizeText(request.Provider),
            ExpiresOn = request.ExpiresOn,
            Notes = ItemWarrantyText.NormalizeText(request.Notes),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        // Item.UpdatedAt is deliberately untouched (notes precedent): recording a warranty is a fact
        // about the item's paperwork, not an edit of the item, so Browse recency stays put.
        db.ItemWarranties.Add(warranty);
        audit.Created("ItemWarranty", warranty.Id, parentEntityId: item.Id);
        await unitOfWork.SaveChangesAsync(ct);

        return ItemWarrantyResponseMapper.ToResponse(warranty, WarrantyStatusRules.TodayUtc(timeProvider));
    }
}
