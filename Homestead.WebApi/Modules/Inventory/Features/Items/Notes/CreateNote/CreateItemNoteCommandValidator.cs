using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.CreateNote;

public class CreateItemNoteCommandValidator : AbstractValidator<CreateItemNoteCommand>
{
    public CreateItemNoteCommandValidator()
    {
        // NotEmpty rejects null and whitespace-only alike, so the trimmed body is never blank.
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("A note needs a body.")
            .MaximumLength(4000);

        RuleFor(x => x.OccurredAt!.Value)
            .NotInFuture()
            .OverridePropertyName(nameof(CreateItemNoteCommand.OccurredAt))
            .When(x => x.OccurredAt.HasValue);
    }
}
