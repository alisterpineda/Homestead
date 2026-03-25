using FluentValidation;

namespace Homestead.WebApi.Features.Admin.Groups.UpdateGroup;

public class UpdateGroupCommandValidator : AbstractValidator<UpdateGroupCommand>
{
    public UpdateGroupCommandValidator()
    {
        RuleFor(x => x.Request.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.");
    }
}
