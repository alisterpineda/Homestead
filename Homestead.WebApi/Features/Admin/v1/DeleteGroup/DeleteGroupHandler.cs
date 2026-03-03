using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.DeleteGroup;

public class DeleteGroupHandler(AppDbContext db)
    : ICommandHandler<DeleteGroupRequest, bool>
{
    public async Task<bool> HandleAsync(DeleteGroupRequest request, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == request.Id, ct);
        if (group is null)
            throw new NotFoundException("Group not found.");

        // Set GroupId to null on affected documents
        await db.Documents
            .Where(d => d.GroupId == request.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.GroupId, (Guid?)null), ct);

        db.Groups.Remove(group);
        await db.SaveChangesAsync(ct);

        return true;
    }
}

public record DeleteGroupRequest(Guid Id);
