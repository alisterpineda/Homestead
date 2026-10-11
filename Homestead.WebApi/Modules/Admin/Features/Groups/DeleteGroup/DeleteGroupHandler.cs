using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.DeleteGroup;

public class DeleteGroupHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<bool> HandleAsync(Guid id, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
            throw new EntityNotFoundException("Group not found.");

        // The change-tracker-bypassing bulk update and the tracked Groups.Remove + flush share one command
        // transaction automatically — CommandWriteCommandInterceptor opens it before the ExecuteUpdate
        // below reaches the database, so the two are atomic without any manual call.

        // Set GroupId to null on affected documents (including soft-deleted ones)
        await db.Documents.IgnoreQueryFilters([DocumentQueryFilters.SoftDelete])
            .Where(d => d.GroupId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.GroupId, (Guid?)null), ct);

        audit.Deleted("Group", group.Id, description: group.Name);

        db.Groups.Remove(group);

        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
