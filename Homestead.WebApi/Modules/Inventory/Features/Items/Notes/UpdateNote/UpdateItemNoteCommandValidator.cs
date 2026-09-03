using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.UpdateNote;

public class UpdateItemNoteCommandValidator : AbstractValidator<UpdateItemNoteCommand>
{
    public UpdateItemNoteCommandValidator()
    {
        RuleFor(x => x.Subject).MaximumLength(200);
        RuleFor(x => x.Body).MaximumLength(4000);

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Subject) || !string.IsNullOrWhiteSpace(x.Body))
            .WithMessage("A note needs a subject or a body.")
            .OverridePropertyName(nameof(UpdateItemNoteCommand.Body));

        RuleFor(x => x.OccurredAt).NotInFuture();
    }
}
