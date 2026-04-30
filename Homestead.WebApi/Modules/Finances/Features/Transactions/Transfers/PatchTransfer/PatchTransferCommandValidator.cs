using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.PatchTransfer;

public class PatchTransferCommandValidator : AbstractValidator<PatchTransferCommand>
{
    public PatchTransferCommandValidator()
    {
        RuleFor(x => x.TransactionId).NotEqual(Guid.Empty);
        RuleFor(x => x.SourceAccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.DestinationAccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.SourceAmount).GreaterThan(0m);
        RuleFor(x => x.DestinationAmount).GreaterThan(0m)
            .When(x => x.DestinationAmount.HasValue);
        RuleFor(x => x.Date).NotInFuture();
        RuleFor(x => x.Memo).MaximumLength(512)
            .When(x => x.Memo is not null);
    }
}
