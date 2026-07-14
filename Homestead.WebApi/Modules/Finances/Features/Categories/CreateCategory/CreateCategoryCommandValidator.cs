using FluentValidation;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.CreateCategory;

public class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must be 256 characters or fewer.");

        RuleFor(x => x.Type)
            .Must(t => t is AccountType.Income or AccountType.Expense)
            .WithMessage("Category type must be Income or Expense.");

        RuleFor(x => x.ParentId).NotEqual(Guid.Empty)
            .When(x => x.ParentId is not null);
    }
}
