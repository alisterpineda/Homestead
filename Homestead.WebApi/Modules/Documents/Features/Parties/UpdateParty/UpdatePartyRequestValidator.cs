using FluentValidation;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.UpdateParty;

public class UpdatePartyRequestValidator : AbstractValidator<UpdatePartyRequest>
{
    public UpdatePartyRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.");
    }
}
