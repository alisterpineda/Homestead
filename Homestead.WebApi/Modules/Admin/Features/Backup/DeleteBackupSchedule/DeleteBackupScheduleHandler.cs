using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Admin.Services;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.DeleteBackupSchedule;

public class DeleteBackupScheduleHandler(IBackupScheduleStore scheduleStore, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteBackupScheduleCommand, bool>
{
    public async Task<bool> Handle(DeleteBackupScheduleCommand request, CancellationToken ct)
    {
        var removed = await scheduleStore.RemoveByTargetAsync(request.TargetKey, ct);
        if (!removed)
            throw new EntityNotFoundException("Backup schedule not found.");

        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
