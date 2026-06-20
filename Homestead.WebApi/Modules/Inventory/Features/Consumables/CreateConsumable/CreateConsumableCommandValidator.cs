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

        // Shape-coherence only: area/container existence and positivity beyond zero are left to
        // StockService (mirroring how ReceiveStockCommand has no validator and relies on the service).
        RuleFor(x => x.InitialQuantity)
            .GreaterThan(0m).WithMessage("Starting quantity must be greater than zero.")
            .When(x => x.InitialQuantity.HasValue);

        RuleFor(x => x)
            .Must(x => x.InitialQuantity.HasValue == (x.InitialAreaId.HasValue || x.InitialContainerItemId.HasValue))
            .WithMessage("A starting location requires a starting quantity, and vice versa.")
            .Must(x => !(x.InitialAreaId.HasValue && x.InitialContainerItemId.HasValue))
            .WithMessage("A starting location must be exactly one of an area or a container.");
    }
}
