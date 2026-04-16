using FluentValidation;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;

public class UpdateGroupCommandValidator : AbstractValidator<UpdateGroupCommand>
{
    public UpdateGroupCommandValidator()
    {
        RuleFor(x => x.Request.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.")
            .MaximumLength(256)
            .WithMessage("Name must not exceed 256 characters.");

        RuleFor(x => x.Request.Description)
            .MaximumLength(1024)
            .WithMessage("Description must not exceed 1024 characters.");
    }
}
