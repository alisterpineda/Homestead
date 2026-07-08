using FluentValidation;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.CreateBrand;

public class CreateBrandCommandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(128).WithMessage("Name must be 128 characters or fewer.");
    }
}
