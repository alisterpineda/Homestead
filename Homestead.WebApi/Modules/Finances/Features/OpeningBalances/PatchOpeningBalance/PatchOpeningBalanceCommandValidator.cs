using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.PatchOpeningBalance;

public class PatchOpeningBalanceCommandValidator : AbstractValidator<PatchOpeningBalanceCommand>
{
    public PatchOpeningBalanceCommandValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Quantity must be positive.");
        RuleFor(x => x.ValuePerUnit).GreaterThan(0m)
            .When(x => x.ValuePerUnit.HasValue)
            .WithMessage("ValuePerUnit, when provided, must be positive.");
        RuleFor(x => x.AsOfDate).NotInFuture();
    }
}
