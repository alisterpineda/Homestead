using FluentValidation;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.PatchCategory;

public class PatchCategoryCommandValidator : AbstractValidator<PatchCategoryCommand>
{
    public PatchCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty);

        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must be 256 characters or fewer.");

        RuleFor(x => x.ParentId).NotEqual(Guid.Empty)
            .When(x => x.ParentId is not null);
    }
}
