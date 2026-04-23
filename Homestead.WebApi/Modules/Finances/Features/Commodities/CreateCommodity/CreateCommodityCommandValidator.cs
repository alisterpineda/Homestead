using FluentValidation;

namespace Homestead.WebApi.Modules.Finances.Features.Commodities.CreateCommodity;

public class CreateCommodityCommandValidator : AbstractValidator<CreateCommodityCommand>
{
    public CreateCommodityCommandValidator()
    {
        RuleFor(x => x.Symbol)
            .Must(s => !string.IsNullOrWhiteSpace(s)).WithMessage("Symbol is required.")
            .MaximumLength(32).WithMessage("Symbol must be 32 characters or fewer.");

        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must be 256 characters or fewer.");

        RuleFor(x => x.DisplayPrecision)
            .InclusiveBetween((short)0, (short)10)
            .WithMessage("DisplayPrecision must be between 0 and 10.");
    }
}
