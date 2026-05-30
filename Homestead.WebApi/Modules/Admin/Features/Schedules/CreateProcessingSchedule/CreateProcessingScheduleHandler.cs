using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;

public class CreateProcessingScheduleHandler(
    AppDbContext db, ICurrentUserAccessor currentUser, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateProcessingScheduleCommand, CreateProcessingScheduleResponse>
{
    public async Task<CreateProcessingScheduleResponse> Handle(
        CreateProcessingScheduleCommand request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var nextRunAt = CronSchedule.NextOccurrence(request.CronExpression, now);

        var schedule = new ProcessingSchedule
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            JobType = request.JobType,
            CronExpression = request.CronExpression,
            TimeZone = CronSchedule.DefaultTimeZone,
            Priority = request.Priority,
            IsEnabled = request.IsEnabled,
            CreatedAt = now,
            CreatedByUserId = currentUser.UserId,
            NextRunAt = nextRunAt,
        };

        db.ProcessingSchedules.Add(schedule);
        await unitOfWork.SaveChangesAsync(ct);

        return new CreateProcessingScheduleResponse
        {
            Id = schedule.Id,
            NextRunAt = schedule.NextRunAt,
        };
    }
}
