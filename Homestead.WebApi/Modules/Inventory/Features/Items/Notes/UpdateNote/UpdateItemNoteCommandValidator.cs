using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Notes.UpdateNote;

public class UpdateItemNoteCommandValidator : AbstractValidator<UpdateItemNoteCommand>
{
    public UpdateItemNoteCommandValidator()
    {
        // NotEmpty rejects null and whitespace-only alike, so the trimmed body is never blank.
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("A note needs a body.")
            .MaximumLength(4000);

        RuleFor(x => x.OccurredAt).NotInFuture();
    }
}
