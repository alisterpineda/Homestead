using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.DeleteGroup;

public class DeleteGroupHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteGroupCommand, bool>
{
    public async Task<bool> Handle(DeleteGroupCommand request, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == request.Id, ct);
        if (group is null)
            throw new EntityNotFoundException("Group not found.");

        // Set GroupId to null on affected documents (including soft-deleted ones)
        await db.Documents.IgnoreQueryFilters()
            .Where(d => d.GroupId == request.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.GroupId, (Guid?)null), ct);

        audit.Deleted("Group", group.Id, description: group.Name);

        db.Groups.Remove(group);

        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}

public record DeleteGroupCommand(Guid Id) : ICommand<bool>;
