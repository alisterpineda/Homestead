using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.PatchBalanceEntry;

public class PatchBalanceEntryCommandValidator : AbstractValidator<PatchBalanceEntryCommand>
{
    public PatchBalanceEntryCommandValidator()
    {
        RuleFor(x => x.TransactionId).NotEqual(Guid.Empty);
        RuleFor(x => x.CommodityId).NotEqual(Guid.Empty);
        RuleFor(x => x.AssertedAmount).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.Date).NotInFuture();
        RuleFor(x => x.Description).MaximumLength(512);
    }
}
