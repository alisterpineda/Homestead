using Cronos;
using FluentValidation;
using Homestead.WebApi.Domain.Processing;

namespace Homestead.WebApi.Modules.Admin.Features.Schedules.CreateProcessingSchedule;

public class CreateProcessingScheduleCommandValidator : AbstractValidator<CreateProcessingScheduleCommand>
{
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
            .Must(BeAParseableCronExpression)
            .WithMessage("Cron expression is not valid.");

        RuleFor(x => x.JobType)
            .Must(t => t == ProcessingJobType.NoOp)
            .WithMessage("Only the NoOp job type is currently supported for admin-created schedules.");
    }

    private static bool BeAParseableCronExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        try
        {
            CronExpression.Parse(expression);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }
}
