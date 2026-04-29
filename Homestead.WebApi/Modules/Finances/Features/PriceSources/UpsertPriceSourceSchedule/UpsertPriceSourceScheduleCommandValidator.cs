using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.UpsertPriceSourceSchedule;

public class UpsertPriceSourceScheduleCommandValidator : AbstractValidator<UpsertPriceSourceScheduleCommand>
{
    public UpsertPriceSourceScheduleCommandValidator()
    {
        RuleFor(x => x.CronExpression)
            .Must(expr => !string.IsNullOrWhiteSpace(expr))
            .WithMessage("Cron expression is required.")
            .Must(CronSchedule.IsValid)
            .WithMessage("Cron expression is not valid.");
    }
}
