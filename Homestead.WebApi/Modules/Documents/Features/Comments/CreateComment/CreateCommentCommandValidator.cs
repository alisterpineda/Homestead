using FluentValidation;

namespace Homestead.WebApi.Modules.Documents.Features.Comments.CreateComment;

public class CreateCommentCommandValidator : AbstractValidator<CreateCommentCommand>
{
    public CreateCommentCommandValidator()
    {
        RuleFor(x => x.Body).NotEmpty().WithMessage("Comment body cannot be empty.");
    }
}
