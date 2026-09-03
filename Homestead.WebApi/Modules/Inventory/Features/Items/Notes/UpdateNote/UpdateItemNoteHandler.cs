using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.UpdateNote;

public class UpdateItemNoteHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateItemNoteCommand, ItemNoteResponse>
{
    public async Task<ItemNoteResponse> Handle(UpdateItemNoteCommand request, CancellationToken ct)
    {
        // Scoped to the route's item so a note id can't be edited through another item's URL.
        var note = await db.ItemNotes.FirstOrDefaultAsync(
            n => n.Id == request.NoteId && n.ItemId == request.ItemId, ct);
        if (note is null)
            throw new EntityNotFoundException("Note not found.");

        audit.Track<ItemNoteSnapshot, ItemNote>(note, "InventoryNote", note.Id, parentEntityId: note.ItemId);

        var subject = ItemNoteText.Normalize(request.Subject);
        var body = ItemNoteText.NormalizeBody(request.Body);
        // Normalised to UTC: Npgsql refuses a non-zero offset on a timestamptz column.
        var occurredAt = request.OccurredAt.ToUniversalTime();

        // A save that changes nothing must not mark the note "edited" (IsEdited = UpdatedAt != CreatedAt),
        // so skip the write entirely; the tracked snapshot diffs to no changes and audits nothing.
        var unchanged = note.OccurredAt == occurredAt && note.Subject == subject && note.Body == body;
        if (!unchanged)
        {
            note.OccurredAt = occurredAt;
            note.Subject = subject;
            note.Body = body;
            note.UpdatedAt = DateTimeOffset.UtcNow;
            note.UpdatedByUserId = currentUser.UserId;
            // Item.UpdatedAt stays untouched — see CreateItemNoteHandler.
            await unitOfWork.SaveChangesAsync(ct);
        }

        return await db.ItemNotes.AsNoTracking()
            .Where(n => n.Id == note.Id)
            .Select(ItemNoteResponseMapper.Projection)
            .FirstAsync(ct);
    }
}
