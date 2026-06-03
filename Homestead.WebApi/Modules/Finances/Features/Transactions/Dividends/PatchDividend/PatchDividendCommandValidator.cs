using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Dividends.PatchDividend;

public class PatchDividendCommandValidator : AbstractValidator<PatchDividendCommand>
{
    public PatchDividendCommandValidator()
    {
        RuleFor(x => x.TransactionId).NotEqual(Guid.Empty);
        RuleFor(x => x.AccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.PaymentCurrencyId).NotEqual(Guid.Empty);
        RuleFor(x => x.Amount).GreaterThan(0m);
        RuleFor(x => x.OccurredAt).NotMoreThanOneDayInFuture();
        RuleFor(x => x.Description).MaximumLength(512)
            .When(x => x.Description is not null);
    }
}
