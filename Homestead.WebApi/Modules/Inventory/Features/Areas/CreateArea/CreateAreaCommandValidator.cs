using FluentValidation;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;

public class CreateAreaCommandValidator : AbstractValidator<CreateAreaCommand>
{
    public CreateAreaCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(128).WithMessage("Name must be 128 characters or fewer.");
    }
}
