using FluentValidation;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.CreatePayee;

public class CreatePayeeCommandValidator : AbstractValidator<CreatePayeeCommand>
{
    public CreatePayeeCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(128).WithMessage("Name must be 128 characters or fewer.");

        RuleFor(x => x.Notes)
            .MaximumLength(1024).WithMessage("Notes must be 1024 characters or fewer.");

        RuleFor(x => x.DefaultCategoryId)
            .NotEqual(Guid.Empty).When(x => x.DefaultCategoryId is not null)
            .WithMessage("DefaultCategoryId must be a valid id.");
    }
}
