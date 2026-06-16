using FluentValidation;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CreateConsumable;

public class CreateConsumableCommandValidator : AbstractValidator<CreateConsumableCommand>
{
    public CreateConsumableCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must be 256 characters or fewer.");

        RuleFor(x => x.Unit)
            .MaximumLength(32).WithMessage("Unit must be 32 characters or fewer.")
            .When(x => !string.IsNullOrWhiteSpace(x.Unit));

        RuleFor(x => x.RestockThreshold)
            .GreaterThanOrEqualTo(0m).WithMessage("Restock threshold must be zero or positive.")
            .When(x => x.RestockThreshold.HasValue);
    }
}
