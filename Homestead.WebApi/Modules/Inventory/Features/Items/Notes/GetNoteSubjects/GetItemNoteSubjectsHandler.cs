using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.GetNoteSubjects;

public class GetItemNoteSubjectsHandler(AppDbContext db)
    : IRequestHandler<GetItemNoteSubjectsQuery, IReadOnlyList<string>?>
{
    public async Task<IReadOnlyList<string>?> Handle(GetItemNoteSubjectsQuery request, CancellationToken ct)
    {
        var exists = await db.Items.AsNoTracking().AnyAsync(i => i.Id == request.ItemId, ct);
        if (!exists)
            return null;

        return await db.ItemNotes.AsNoTracking()
            .Where(n => n.ItemId == request.ItemId && n.Subject != null)
            .GroupBy(n => n.Subject!)
            .Select(g => new { Subject = g.Key, Latest = g.Max(n => n.OccurredAt) })
            .OrderByDescending(x => x.Latest)
            .ThenBy(x => x.Subject)
            .Select(x => x.Subject)
            .ToListAsync(ct);
    }
}
