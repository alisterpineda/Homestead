using FluentValidation;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.GetPartiesByIds;

public class GetPartiesByIdsQueryValidator : AbstractValidator<GetPartiesByIdsQuery>
{
    public GetPartiesByIdsQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotEmpty()
            .WithMessage("At least one party ID must be provided.")
            .Must(ids => ids.Count <= 200)
            .WithMessage("Cannot resolve more than 200 party IDs at once.");
    }
}
