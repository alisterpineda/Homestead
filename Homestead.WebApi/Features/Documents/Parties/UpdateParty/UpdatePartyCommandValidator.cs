using FluentValidation;

namespace Homestead.WebApi.Features.Documents.Parties.UpdateParty;

public class UpdatePartyCommandValidator : AbstractValidator<UpdatePartyCommand>
{
    public UpdatePartyCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.");
    }
}
