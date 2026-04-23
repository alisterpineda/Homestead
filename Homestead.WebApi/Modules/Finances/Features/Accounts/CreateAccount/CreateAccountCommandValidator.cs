using FluentValidation;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.CreateAccount;

public class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must be 256 characters or fewer.");

        RuleFor(x => x.AccountType)
            .Must(t => t is AccountType.Asset or AccountType.Liability or AccountType.Equity)
            .WithMessage("AccountType must be Asset, Liability, or Equity. Income and Expense are reserved for a future release.");

        RuleFor(x => x.DefaultCommodityId)
            .NotEqual(Guid.Empty).WithMessage("DefaultCommodityId is required.");

        RuleFor(x => x.Institution)
            .MaximumLength(128).WithMessage("Institution must be 128 characters or fewer.");
    }
}
