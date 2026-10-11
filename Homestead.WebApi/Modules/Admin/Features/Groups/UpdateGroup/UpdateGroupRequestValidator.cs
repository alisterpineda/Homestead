using FluentValidation;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;

public class UpdateGroupRequestValidator : AbstractValidator<UpdateGroupRequest>
{
    public UpdateGroupRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.")
            .MaximumLength(256)
            .WithMessage("Name must not exceed 256 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1024)
            .WithMessage("Description must not exceed 1024 characters.");
    }
}
