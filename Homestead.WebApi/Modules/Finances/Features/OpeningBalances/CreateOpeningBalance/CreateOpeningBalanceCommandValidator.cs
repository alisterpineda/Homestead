using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.OpeningBalances.CreateOpeningBalance;

public class CreateOpeningBalanceCommandValidator : AbstractValidator<CreateOpeningBalanceCommand>
{
    public CreateOpeningBalanceCommandValidator()
    {
        RuleFor(x => x.CommodityId).NotEqual(Guid.Empty).WithMessage("CommodityId is required.");
        RuleFor(x => x.Description).MaximumLength(512);
        RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Quantity must be positive.");
        RuleFor(x => x.ValuePerUnit).GreaterThan(0m)
            .When(x => x.ValuePerUnit.HasValue)
            .WithMessage("ValuePerUnit, when provided, must be positive.");
        RuleFor(x => x.AsOfDate).NotInFuture();
    }
}
