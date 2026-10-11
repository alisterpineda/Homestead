using FluentValidation;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.TrackDurable;

public class TrackDurableRequestValidator : AbstractValidator<TrackDurableRequest>
{
    public TrackDurableRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must be 256 characters or fewer.");

        // The source is exactly one of an area or a container.
        RuleFor(x => x)
            .Must(b => (b.AreaId is null) ^ (b.ContainerItemId is null))
            .WithMessage("A source location must be exactly one of an area or a container.");

        RuleFor(x => x.Compartment)
            .MaximumLength(256).WithMessage("Compartment must be 256 characters or fewer.");

        // Compartment is a sub-position within a container — it makes no sense without one.
        RuleFor(x => x.Compartment)
            .Null().WithMessage("Compartment can only be set when the source is a container.")
            .When(x => x.ContainerItemId is null);
    }
}
