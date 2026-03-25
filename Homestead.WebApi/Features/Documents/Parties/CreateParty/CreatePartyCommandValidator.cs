using FluentValidation;

namespace Homestead.WebApi.Features.Documents.Parties.CreateParty;

public class CreatePartyCommandValidator : AbstractValidator<CreatePartyCommand>
{
    public CreatePartyCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.");
    }
}
