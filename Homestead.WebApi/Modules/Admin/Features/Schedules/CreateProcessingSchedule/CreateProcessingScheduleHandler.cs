using Cronos;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;

public class CreateProcessingScheduleHandler(AppDbContext db, ICurrentUserAccessor currentUser)
    : IRequestHandler<CreateProcessingScheduleCommand, CreateProcessingScheduleResponse>
{
    public async Task<CreateProcessingScheduleResponse> Handle(
        CreateProcessingScheduleCommand request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var nextRunAt = ComputeNextOccurrence(request.CronExpression, now);

        var schedule = new ProcessingSchedule
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            JobType = request.JobType,
            CronExpression = request.CronExpression,
            TimeZone = "Etc/UTC",
            Priority = request.Priority,
            IsEnabled = request.IsEnabled,
            CreatedAt = now,
            CreatedByUserId = currentUser.UserId,
            NextRunAt = nextRunAt,
        };

        db.ProcessingSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);

        return new CreateProcessingScheduleResponse
        {
            Id = schedule.Id,
            NextRunAt = schedule.NextRunAt,
        };
    }

    private static DateTimeOffset ComputeNextOccurrence(string cronExpression, DateTimeOffset from)
    {
        var expr = CronExpression.Parse(cronExpression);
        var next = expr.GetNextOccurrence(from, TimeZoneInfo.Utc)
            ?? throw new InvalidOperationException(
                $"Cron expression '{cronExpression}' has no future occurrences after {from:O}");
        return next;
    }
}
