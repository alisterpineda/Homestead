using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.CreateDurable;

public class CreateDurableCommandValidator : AbstractValidator<CreateDurableCommand>
{
    public CreateDurableCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must be 256 characters or fewer.");

        // The default JsonStringEnumConverter binds arbitrary integers to DurableKind; reject an
        // out-of-range value so it can't persist and later re-serialize as a JSON number.
        RuleFor(x => x.DurableKind).IsInEnum();

        RuleFor(x => x.Serial)
            .MaximumLength(128).WithMessage("Serial must be 128 characters or fewer.");

        RuleFor(x => x.Model)
            .MaximumLength(128).WithMessage("Model must be 128 characters or fewer.");

        // At most one location: an item is created in an area, in a container, or nowhere.
        RuleFor(x => x.ParentItemId)
            .Null().WithMessage("An item can be created in an area or a container, not both.")
            .When(x => x.AreaId.HasValue);

        RuleFor(x => x.Compartment)
            .MaximumLength(256).WithMessage("Compartment must be 256 characters or fewer.");

        // Compartment is a sub-position within a container — it makes no sense without one.
        // (Auto-clearing on unplace is a patch-only idiom; on create we hard-reject.)
        RuleFor(x => x.Compartment)
            .Null().WithMessage("Compartment can only be set when the item is in a container.")
            .When(x => !x.ParentItemId.HasValue);

        RuleFor(x => x.AcquiredOn!.Value)
            .NotInFuture()
            .OverridePropertyName(nameof(CreateDurableCommand.AcquiredOn))
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
