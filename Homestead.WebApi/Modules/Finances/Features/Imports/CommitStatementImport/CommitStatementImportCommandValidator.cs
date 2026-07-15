using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.CommitStatementImport;

public class CommitStatementImportCommandValidator : AbstractValidator<CommitStatementImportCommand>
{
    public CommitStatementImportCommandValidator()
    {
        RuleFor(x => x.Memo).MaximumLength(512);

        // Amount/date only matter when the reviewer accepts the assertion.
        When(x => x.Accepted, () =>
        {
            RuleFor(x => x.AssertedAmount).GreaterThanOrEqualTo(0m)
                .WithMessage("AssertedAmount must be non-negative (use 0 for emptied accounts).");
            RuleFor(x => x.Date).NotInFuture();
        });

        // Every curated line becomes a real flow — validate each as strictly as the flow endpoints do.
        RuleForEach(x => x.LineItems).ChildRules(line =>
        {
            line.RuleFor(l => l.Amount).GreaterThan(0m)
                .WithMessage("Line amount must be greater than zero.");
            line.RuleFor(l => l.Date).NotInFuture();
            line.RuleFor(l => l.Description).MaximumLength(512);
            line.RuleFor(l => l.Direction).IsInEnum();
        });
    }
}
