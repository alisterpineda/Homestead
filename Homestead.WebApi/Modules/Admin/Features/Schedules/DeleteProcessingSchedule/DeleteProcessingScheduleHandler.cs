using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.DeleteProcessingSchedule;

public class DeleteProcessingScheduleHandler(AppDbContext db, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteProcessingScheduleCommand, bool>
{
    public async Task<bool> Handle(DeleteProcessingScheduleCommand request, CancellationToken ct)
    {
        // Open the lazy command transaction before the change-tracker-bypassing ExecuteDelete, so it stays
        // transactional (today's eager tx wrapped it; under naive lazy-tx it would autocommit standalone).
        await unitOfWork.EnsureTransactionAsync(ct);

        var affected = await db.ProcessingSchedules
            .Where(s => s.Id == request.Id)
            .ExecuteDeleteAsync(ct);

        if (affected == 0)
            throw new EntityNotFoundException("Processing schedule not found.");

        return true;
    }
}

public record DeleteProcessingScheduleCommand(Guid Id) : ICommand<bool>;
