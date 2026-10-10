using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.UpdateWarranty;

public class UpdateItemWarrantyHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IValidator<UpdateItemWarrantyCommand> validator)
{
    public async Task<ItemWarrantyResponse> Handle(UpdateItemWarrantyCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        // Scoped to the route's item so a warranty id can't be edited through another item's URL.
        var warranty = await db.ItemWarranties.FirstOrDefaultAsync(
            w => w.Id == request.WarrantyId && w.ItemId == request.ItemId, ct);
        if (warranty is null)
            throw new EntityNotFoundException("Warranty not found.");

        // Tracked before the mutation: the snapshot captures the "before" values eagerly and diffs
        // against the entity's final state at flush time.
        audit.Track<ItemWarrantySnapshot, ItemWarranty>(
            warranty, "ItemWarranty", warranty.Id, parentEntityId: warranty.ItemId);

        var provider = ItemWarrantyText.NormalizeText(request.Provider);
        var notes = ItemWarrantyText.NormalizeText(request.Notes);

        // A save that changes nothing must not bump UpdatedAt, so skip the write entirely; the tracked
        // snapshot then diffs to no changes and audits nothing (notes precedent).
        var unchanged = warranty.Provider == provider
            && warranty.ExpiresOn == request.ExpiresOn
            && warranty.Notes == notes;
        if (!unchanged)
        {
            warranty.Provider = provider;
            warranty.ExpiresOn = request.ExpiresOn;
            warranty.Notes = notes;
            warranty.UpdatedAt = DateTimeOffset.UtcNow;
            warranty.UpdatedByUserId = currentUser.UserId;
            // Item.UpdatedAt stays untouched — see CreateItemWarrantyHandler.
            await unitOfWork.SaveChangesAsync(ct);
        }

        return ItemWarrantyResponseMapper.ToResponse(warranty, WarrantyStatusRules.TodayUtc(timeProvider));
    }
}
