using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.CreateBalanceEntry;

public class CreateBalanceEntryCommandValidator : AbstractValidator<CreateBalanceEntryCommand>
{
    public CreateBalanceEntryCommandValidator()
    {
        RuleFor(x => x.CommodityId).NotEqual(Guid.Empty).WithMessage("CommodityId is required.");
        RuleFor(x => x.AssertedAmount).GreaterThanOrEqualTo(0m)
            .WithMessage("AssertedAmount must be non-negative (use 0 for emptied accounts).");
        RuleFor(x => x.Date).NotInFuture();
        RuleFor(x => x.Note).MaximumLength(1024)
            .When(x => x.Note is not null);
    }
}
