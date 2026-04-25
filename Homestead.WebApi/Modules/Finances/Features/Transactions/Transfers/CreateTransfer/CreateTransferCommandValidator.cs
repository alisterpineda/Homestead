using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Transfers.CreateTransfer;

public class CreateTransferCommandValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferCommandValidator()
    {
        RuleFor(x => x.SourceAccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.DestinationAccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.SourceAmount).GreaterThan(0m);
        RuleFor(x => x.DestinationAmount).GreaterThan(0m)
            .When(x => x.DestinationAmount.HasValue);
        RuleFor(x => x.Date).NotInFuture();
        RuleFor(x => x.Description).MaximumLength(512)
            .When(x => x.Description is not null);
    }
}
