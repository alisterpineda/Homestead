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
            // Index recovers the staged raw text for learning; a negative index can't match any staged line.
            // A manual line carries a null index (no staged origin), so the rule only applies when one is set.
            line.RuleFor(l => l.Index).GreaterThanOrEqualTo(0).When(l => l.Index is not null);
            line.RuleFor(l => l.Amount).GreaterThan(0m)
                .WithMessage("Line amount must be greater than zero.");
            line.RuleFor(l => l.Date).NotInFuture();
            line.RuleFor(l => l.Description).MaximumLength(512);
            line.RuleFor(l => l.Direction).IsInEnum();
            // null = no payee; a supplied payee must be a real id (FlowService validates existence).
            line.RuleFor(l => l.PayeeId).NotEqual(Guid.Empty).When(l => l.PayeeId is not null);
            // null = Uncategorised; a supplied category must be a real id. Direction-match / existence /
            // archived checks need the loaded category type, so they live in the handler's pre-validate.
            line.RuleFor(l => l.CategoryId).NotEqual(Guid.Empty).When(l => l.CategoryId is not null);
        });
    }
}
