using FluentValidation;

namespace Homestead.WebApi.Features.Documents.v1.CreateParty;

public class CreatePartyRequestValidator : AbstractValidator<CreatePartyRequest>
{
    public CreatePartyRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.");
    }
}
