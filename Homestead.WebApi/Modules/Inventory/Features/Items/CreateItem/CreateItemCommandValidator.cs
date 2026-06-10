using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.CreateItem;

public class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must be 256 characters or fewer.");

        RuleFor(x => x.Serial)
            .MaximumLength(128).WithMessage("Serial must be 128 characters or fewer.");

        RuleFor(x => x.AcquiredOn!.Value)
            .NotInFuture()
            .OverridePropertyName(nameof(CreateItemCommand.AcquiredOn))
            .When(x => x.AcquiredOn.HasValue);

        RuleFor(x => x.Value)
            .GreaterThanOrEqualTo(0m).WithMessage("Value must be zero or positive.")
            .When(x => x.Value.HasValue);

        // Value and its currency are set together or not at all.
        RuleFor(x => x.ValueCurrencyId)
            .NotNull().WithMessage("ValueCurrencyId is required when Value is set.")
            .When(x => x.Value.HasValue);
        RuleFor(x => x.Value)
            .NotNull().WithMessage("Value is required when ValueCurrencyId is set.")
            .When(x => x.ValueCurrencyId.HasValue);
    }
}
