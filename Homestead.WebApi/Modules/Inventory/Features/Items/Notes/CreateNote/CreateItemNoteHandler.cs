using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.CreateNote;

public class CreateItemNoteHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateItemNoteCommand, ItemNoteResponse>
{
    public async Task<ItemNoteResponse> Handle(CreateItemNoteCommand request, CancellationToken ct)
    {
        // Any kind, any status: a sold or archived item can still have its story completed
        // (activity.md §Which items can carry notes).
        var itemExists = await db.Items.AnyAsync(i => i.Id == request.ItemId, ct);
        if (!itemExists)
            throw new EntityNotFoundException("Item not found.");

        var now = DateTimeOffset.UtcNow;
        var note = new ItemNote
        {
            Id = Guid.NewGuid(),
            ItemId = request.ItemId,
            // Normalised to UTC: Npgsql refuses a non-zero offset on a timestamptz column.
            OccurredAt = (request.OccurredAt ?? now).ToUniversalTime(),
            Subject = ItemNoteText.Normalize(request.Subject),
            Body = ItemNoteText.NormalizeBody(request.Body),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = currentUser.UserId,
            UpdatedByUserId = currentUser.UserId,
        };

        // Item.UpdatedAt is deliberately untouched: a note is history about the item, not an edit of it,
        // so it must not bump the item in Browse's recency order.
        db.ItemNotes.Add(note);
        audit.Created("InventoryNote", note.Id, parentEntityId: note.ItemId);
        await unitOfWork.SaveChangesAsync(ct);

        return await db.ItemNotes.AsNoTracking()
            .Where(n => n.Id == note.Id)
            .Select(ItemNoteResponseMapper.Projection)
            .FirstAsync(ct);
    }
}
