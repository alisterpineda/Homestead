using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;

public class CreateProcessingScheduleHandler(
    AppDbContext db, ICurrentUserAccessor currentUser, IUnitOfWork unitOfWork,
    IValidator<CreateProcessingScheduleCommand> validator)
{
    public async Task<CreateProcessingScheduleResponse> HandleAsync(
        CreateProcessingScheduleCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var now = DateTimeOffset.UtcNow;
        var nextRunAt = CronSchedule.NextOccurrence(request.CronExpression, now);

        var schedule = new ProcessingSchedule
        {
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
