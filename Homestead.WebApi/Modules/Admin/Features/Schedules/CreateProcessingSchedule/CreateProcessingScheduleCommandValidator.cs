using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;

public class CreateProcessingScheduleCommandValidator : AbstractValidator<CreateProcessingScheduleCommand>
{
    private static readonly HashSet<ProcessingJobType> _allowedJobTypes =
    [
        ProcessingJobType.NoOp,
        ProcessingJobType.ImportCommodityPrices,
    ];

    public CreateProcessingScheduleCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.")
            .MaximumLength(256)
            .WithMessage("Name must not exceed 256 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1024)
            .WithMessage("Description must not exceed 1024 characters.");

        RuleFor(x => x.CronExpression)
            .Must(expr => !string.IsNullOrWhiteSpace(expr))
            .WithMessage("Cron expression is required.")
            .Must(CronSchedule.IsValid)
            .WithMessage("Cron expression is not valid.");

        RuleFor(x => x.JobType)
            .Must(t => _allowedJobTypes.Contains(t))
            .WithMessage("This job type cannot be scheduled from the admin endpoint.");

        RuleFor(x => x.JobType)
            .Must(t => t != ProcessingJobType.ImportCommodityPrices)
            .WithMessage("Use /api/v1/finances/settings/price-sources/{source}/schedule to manage commodity-price import schedules.");
    }
}
