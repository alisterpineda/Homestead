using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.DeleteProcessingSchedule;

public class DeleteProcessingScheduleHandler(AppDbContext db)
    : IRequestHandler<DeleteProcessingScheduleCommand, bool>
{
    public async Task<bool> Handle(DeleteProcessingScheduleCommand request, CancellationToken ct)
    {
        // The change-tracker-bypassing ExecuteDelete is made transactional automatically:
        // CommandWriteCommandInterceptor opens the command tx as the DELETE reaches the database (the match
        // count is not yet known at that point), so it rolls back with the command on any later failure —
        // e.g. the not-found throw below leaves no committed effect.
        var affected = await db.ProcessingSchedules
            .Where(s => s.Id == request.Id)
            .ExecuteDeleteAsync(ct);

        if (affected == 0)
            throw new EntityNotFoundException("Processing schedule not found.");

        return true;
    }
}

public record DeleteProcessingScheduleCommand(Guid Id) : ICommand<bool>;
