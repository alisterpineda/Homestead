using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.CreateAccount;

public class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must be 256 characters or fewer.");

        RuleFor(x => x.Kind)
            .IsInEnum().WithMessage("Kind must be a valid account kind.");

        RuleFor(x => x.ReportingCurrencyId)
            .NotEqual(Guid.Empty).WithMessage("ReportingCurrencyId is required.");

        RuleFor(x => x.Institution)
            .MaximumLength(128).WithMessage("Institution must be 128 characters or fewer.");

        RuleFor(x => x.InitialBalance)
            .NotNull().WithMessage("InitialBalance is required for non-Investment account kinds.")
            .When(x => x.Kind != AccountKind.Investment);

        RuleFor(x => x.InitialBalance)
            .Null().WithMessage("InitialBalance must be omitted for Investment accounts — add positions individually after creation.")
            .When(x => x.Kind == AccountKind.Investment);

        When(x => x.InitialBalance is not null, () =>
        {
            RuleFor(x => x.InitialBalance!.Amount)
                .GreaterThanOrEqualTo(0m).WithMessage("InitialBalance.Amount must be zero or positive.");
            RuleFor(x => x.InitialBalance!.AsOfDate).NotInFuture();
        });
    }
}
