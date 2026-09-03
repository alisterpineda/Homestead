using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.CreateNote;

public class CreateItemNoteCommandValidator : AbstractValidator<CreateItemNoteCommand>
{
    public CreateItemNoteCommandValidator()
    {
        RuleFor(x => x.Subject).MaximumLength(200);
        RuleFor(x => x.Body).MaximumLength(4000);

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Subject) || !string.IsNullOrWhiteSpace(x.Body))
            .WithMessage("A note needs a subject or a body.")
            .OverridePropertyName(nameof(CreateItemNoteCommand.Body));

        RuleFor(x => x.OccurredAt!.Value)
            .NotInFuture()
            .OverridePropertyName(nameof(CreateItemNoteCommand.OccurredAt))
            .When(x => x.OccurredAt.HasValue);
    }
}
