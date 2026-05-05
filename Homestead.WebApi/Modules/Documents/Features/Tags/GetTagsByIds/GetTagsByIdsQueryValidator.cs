using FluentValidation;

namespace Homestead.WebApi.Modules.Documents.Features.Tags.GetTagsByIds;

public class GetTagsByIdsQueryValidator : AbstractValidator<GetTagsByIdsQuery>
{
    public GetTagsByIdsQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotEmpty()
            .WithMessage("At least one tag ID must be provided.")
            .Must(ids => ids.Count <= 200)
            .WithMessage("Cannot resolve more than 200 tag IDs at once.");
    }
}
