using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.DeleteGroup;

public class DeleteGroupHandler(AppDbContext db)
    : ICommandHandler<Guid, IResult>
{
    public async Task<IResult> HandleAsync(Guid groupId, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null)
            return Results.NotFound();

        // Set GroupId to null on affected documents
        await db.Documents
            .Where(d => d.GroupId == groupId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.GroupId, (Guid?)null), ct);

        db.Groups.Remove(group);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
