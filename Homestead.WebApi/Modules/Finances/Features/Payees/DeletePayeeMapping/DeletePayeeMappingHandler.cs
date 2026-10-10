using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.DeletePayeeMapping;

/// <summary>
/// Hard-delete a single learned mapping (the reviewer pruning a wrong or stale fingerprint). Unlike the
/// machine-written learning path, an explicit human delete IS audited.
/// </summary>
public class DeletePayeeMappingHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<bool> Handle(DeletePayeeMappingCommand request, CancellationToken ct)
    {
        var mapping = await db.PayeeMappings.FirstOrDefaultAsync(m => m.Id == request.Id, ct);
        if (mapping is null)
            return false;

        db.PayeeMappings.Remove(mapping);
        audit.Deleted("PayeeMapping", mapping.Id, mapping.SampleRawDescription);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
