using FluentValidation;

namespace Homestead.WebApi.Features.Documents.v1.CreateComment;

public class CreateCommentCommandValidator : AbstractValidator<CreateCommentCommand>
{
    public CreateCommentCommandValidator()
    {
        RuleFor(x => x.Body).NotEmpty().WithMessage("Comment body cannot be empty.");
    }
}
