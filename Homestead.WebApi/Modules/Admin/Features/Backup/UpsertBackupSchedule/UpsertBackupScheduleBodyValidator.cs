using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.UpsertBackupSchedule;

public class UpsertBackupScheduleBodyValidator : AbstractValidator<UpsertBackupScheduleBody>
{
    public UpsertBackupScheduleBodyValidator()
    {
        RuleFor(x => x.CronExpression)
            .Must(expr => !string.IsNullOrWhiteSpace(expr))
            .WithMessage("Cron expression is required.")
            .Must(CronSchedule.IsValid)
            .WithMessage("Cron expression is not valid.");
    }
}
