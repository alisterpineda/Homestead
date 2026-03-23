using FluentValidation;

namespace Homestead.WebApi.Features.Documents.v1.CreateComment;

public class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().WithMessage("Comment body cannot be empty.");
    }
}
