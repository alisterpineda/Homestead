using FluentValidation;

namespace Homestead.WebApi.Features.Documents.v1.CreateParty;

public class CreatePartyCommandValidator : AbstractValidator<CreatePartyCommand>
{
    public CreatePartyCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.");
    }
}
