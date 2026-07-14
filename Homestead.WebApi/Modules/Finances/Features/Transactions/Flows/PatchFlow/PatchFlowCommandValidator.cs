using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Flows.PatchFlow;

public class PatchFlowCommandValidator : AbstractValidator<PatchFlowCommand>
{
    public PatchFlowCommandValidator()
    {
        RuleFor(x => x.TransactionId).NotEqual(Guid.Empty);
        RuleFor(x => x.AccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.Amount).GreaterThan(0m);
        RuleFor(x => x.Date).NotInFuture();
        RuleFor(x => x.Description).MaximumLength(512)
            .When(x => x.Description is not null);
        // null = Uncategorised; a supplied category must be a real id (the resolver validates the rest).
        RuleFor(x => x.CategoryId).NotEqual(Guid.Empty)
            .When(x => x.CategoryId is not null);
    }
}
