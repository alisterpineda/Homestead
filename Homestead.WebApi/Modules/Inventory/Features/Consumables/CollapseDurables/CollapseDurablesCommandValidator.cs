using FluentValidation;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.CollapseDurables;

public class CollapseDurablesCommandValidator : AbstractValidator<CollapseDurablesCommand>
{
    public CollapseDurablesCommandValidator()
    {
        // Cascade.Stop: when ItemIds is null/empty the NotEmpty failure must short-circuit, or the
        // duplicate-check below would dereference a null list and throw (an unhandled 500, not a 400).
        RuleFor(x => x.Body.ItemIds)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("At least one item must be selected to collapse.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("The selected items must not contain duplicates.");

        // The target is exactly one of an area or a container.
        RuleFor(x => x.Body)
            .Must(b => (b.AreaId is null) ^ (b.ContainerItemId is null))
            .WithMessage("A target location must be exactly one of an area or a container.");
    }
}
