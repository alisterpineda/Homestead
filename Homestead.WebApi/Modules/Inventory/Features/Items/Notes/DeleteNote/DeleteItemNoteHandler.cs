using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.DeleteNote;

public class DeleteItemNoteHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<bool> Handle(DeleteItemNoteCommand request, CancellationToken ct)
    {
        var note = await db.ItemNotes.FirstOrDefaultAsync(
            n => n.Id == request.NoteId && n.ItemId == request.ItemId, ct);
        if (note is null)
            return false;

        // A real delete — no trash, no tombstone (activity.md §Notes are editable and deletable / §Sync).
        // The audit row keeps a one-line description so the deletion is still traceable.
        db.ItemNotes.Remove(note);
        audit.Deleted("InventoryNote", note.Id,
            ItemNoteText.AuditDescription(note.Body), parentEntityId: note.ItemId);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
