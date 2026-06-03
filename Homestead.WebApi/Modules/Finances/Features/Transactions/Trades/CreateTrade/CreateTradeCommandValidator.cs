using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Trades.CreateTrade;

public class CreateTradeCommandValidator : AbstractValidator<CreateTradeCommand>
{
    public CreateTradeCommandValidator()
    {
        RuleFor(x => x.AccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.Side).IsInEnum();
        RuleFor(x => x.SecurityId).NotEqual(Guid.Empty);
        RuleFor(x => x.FundingCurrencyId).NotEqual(Guid.Empty);
        RuleFor(x => x.Quantity).GreaterThan(0m);
        RuleFor(x => x.UnitPrice).GreaterThan(0m);
        RuleFor(x => x.OccurredAt).NotMoreThanOneDayInFuture();
        RuleFor(x => x.Memo).MaximumLength(512)
            .When(x => x.Memo is not null);
    }
}
